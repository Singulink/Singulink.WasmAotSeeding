using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Singulink.WasmAotSeeding.PackageTests.Infrastructure;

namespace Singulink.WasmAotSeeding.PackageTests;

/// <summary>
/// The package's layout and metadata, and a project that restores it from a feed as its users do.
/// </summary>
[TestClass]
public class PackageTests
{
    // Build logic and the generator only: nothing that a project would reference, and no dependencies, as the generator's are in tools/ with it.
    // Of Mono.Cecil's assemblies, only Mono.Cecil.dll, as the generator reads no symbols and doesn't use Mono.Cecil.Rocks.
    [TestMethod]
    public void When_Packed_Then_The_Package_Has_The_Build_Files_And_The_Generator_Only()
    {
        using var package = ZipFile.OpenRead(TestEnvironment.PackageFile);
        string[] files = package.Entries
            .Select(e => e.FullName)
            .Where(n => !n.StartsWith("_rels/", StringComparison.Ordinal) && !n.StartsWith("package/", StringComparison.Ordinal))
            .Where(n => n != "[Content_Types].xml")
            .Order(StringComparer.Ordinal)
            .ToArray();

        files.ShouldBe([
            "README.md",
            "Singulink Icon 128x128.png",
            "Singulink.WasmAotSeeding.nuspec",
            "build/Singulink.WasmAotSeeding.props",
            "build/Singulink.WasmAotSeeding.targets",
            "tools/net10.0/Mono.Cecil.dll",
            "tools/net10.0/Singulink.WasmAotSeeding.deps.json",
            "tools/net10.0/Singulink.WasmAotSeeding.dll",
            "tools/net10.0/Singulink.WasmAotSeeding.pdb",
            "tools/net10.0/Singulink.WasmAotSeeding.runtimeconfig.json",
        ]);
    }

    [TestMethod]
    public void When_Packed_Then_The_Package_Is_A_Development_Dependency_Without_Dependencies()
    {
        using var package = ZipFile.OpenRead(TestEnvironment.PackageFile);
        using var nuspec = package.GetEntry("Singulink.WasmAotSeeding.nuspec")!.Open();
        var metadata = XDocument.Load(nuspec).Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value;

        Value("id").ShouldBe(TestEnvironment.PackageId);
        Value("version").ShouldBe(TestEnvironment.PackageVersion);
        Value("developmentDependency").ShouldBe("true");
        Value("license").ShouldBe("MIT");
        Value("readme").ShouldBe("README.md");
        Value("icon").ShouldBe("Singulink Icon 128x128.png");
        Value("projectUrl").ShouldBe("https://github.com/Singulink/Singulink.WasmAotSeeding");
        metadata.Elements().ShouldNotContain(e => e.Name.LocalName == "dependencies");
    }

    // The generator runs on a later .NET too, and the stamp of the seeding has the package version.
    [TestMethod]
    public void When_Packed_Then_The_Generator_Rolls_Forward_And_The_Build_Files_Have_The_Version()
    {
        string tools = Path.Combine(TestEnvironment.ExtractedPackageDirectory, "tools", "net10.0");
        using var runtimeConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(tools, "Singulink.WasmAotSeeding.runtimeconfig.json")));
        var options = runtimeConfig.RootElement.GetProperty("runtimeOptions");

        options.GetProperty("rollForward").GetString().ShouldBe("Major");
        options.GetProperty("framework").GetProperty("name").GetString().ShouldBe("Microsoft.NETCore.App");

        string props = File.ReadAllText(Path.Combine(TestEnvironment.ExtractedPackageDirectory, "build", "Singulink.WasmAotSeeding.props"));
        props.ShouldContain($"<_AotWrapperSeedingVersion>{TestEnvironment.PackageVersion}</_AotWrapperSeedingVersion>", Case.Sensitive);
    }

    // A project without the browserwasm platform: NuGet imports the build files, which do nothing, and adds no reference.
    [TestMethod]
    public void When_A_Project_References_The_Package_Then_It_Restores_And_Builds_Without_Seeding()
    {
        string directory = Path.Combine(TestEnvironment.RootDirectory, "restore-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        string projectFile = Path.Combine(directory, "Library.csproj");

        string project = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RunAOTCompilation>true</RunAOTCompilation>
                <WasmAotProfilePath>aot.profile</WasmAotProfilePath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{TestEnvironment.PackageId}" Version="{TestEnvironment.PackageVersion}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(projectFile, project);
        File.WriteAllText(Path.Combine(directory, "Class.cs"), "namespace Library; public static class Class { }");

        try
        {
            var build = BuildRunner.MSBuild(projectFile, directory, ["-restore", "-p:Configuration=Release"]);

            build.Succeeded.ShouldBeTrue(build.Output);
            build.Codes.ShouldBeEmpty(build.Output);
            build.SeedingLines.ShouldBeEmpty(build.Output);

            var version = BuildRunner.MSBuild(projectFile, directory, ["-getProperty:_AotWrapperSeedingVersion"]);
            version.Output.Trim().ShouldBe(TestEnvironment.PackageVersion, version.Output);

            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "obj", "project.assets.json")));
            var library = assets.RootElement.GetProperty("targets").GetProperty("net10.0")
                .GetProperty($"{TestEnvironment.PackageId}/{TestEnvironment.PackageVersion}");

            library.TryGetProperty("compile", out _).ShouldBeFalse();
            library.TryGetProperty("runtime", out _).ShouldBeFalse();
            library.GetProperty("build").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe([
                "build/Singulink.WasmAotSeeding.props",
                "build/Singulink.WasmAotSeeding.targets",
            ]);
        }
        finally
        {
            TestEnvironment.TryDeleteDirectory(directory);
        }
    }
}
