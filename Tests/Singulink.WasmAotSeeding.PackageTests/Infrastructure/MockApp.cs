using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Singulink.WasmAotSeeding.PackageTests.Infrastructure;

/// <summary>
/// A WebAssembly app that imports the package's build files as NuGet does, and the mock .NET WebAssembly SDK (see
/// <see cref="TestEnvironment.MockSdkFile"/>), in a folder of its own with a space in its name. It AOT-compiles the core library of the runtime that
/// runs the tests (<c>MockAotInputs</c>), with an AOT profile, and checks with the fake llvm-nm.
/// </summary>
internal sealed class MockApp : IDisposable
{
    public MockApp(string name)
    {
        RootDirectory = Path.Combine(TestEnvironment.RootDirectory, $"{name}-{Guid.NewGuid().ToString("N")[..8]}");
        ProjectDirectory = Path.Combine(RootDirectory, "mock app");
        Directory.CreateDirectory(ProjectDirectory);

        // The AOT profile isn't read: the mock compilation only records the profiles it's given.
        File.WriteAllText(Path.Combine(ProjectDirectory, "aot.profile"), string.Empty);

        string build = Path.Combine(TestEnvironment.ExtractedPackageDirectory, "build", TestEnvironment.PackageId);
        string project = $"""
            <Project>
              <Import Project="{build}.props" />

              <PropertyGroup>
                <TargetFramework>net10.0-browserwasm</TargetFramework>
                <IntermediateOutputPath>obj\</IntermediateOutputPath>
                <RunAOTCompilation>true</RunAOTCompilation>
                <WasmAotProfilePath>$(MSBuildProjectDirectory)\aot.profile</WasmAotProfilePath>
                <MockAotInputs>{TestEnvironment.CoreLibrary}</MockAotInputs>
              </PropertyGroup>

              <Import Project="{TestEnvironment.MockSdkFile}" />
              <Import Project="{build}.targets" />
            </Project>
            """;

        File.WriteAllText(ProjectFile, project);
    }

    public string RootDirectory { get; }

    public string ProjectDirectory { get; }

    public string ProjectFile => Path.Combine(ProjectDirectory, "App.proj");

    /// <summary>
    /// Gets the folder of the seeding's files.
    /// </summary>
    public string SeedingDirectory => Path.Combine(ProjectDirectory, "obj", "wasm-aot-seeding");

    public string SeedAssembly => Path.Combine(SeedingDirectory, "AotWrapperSeeds.dll");

    public string Stamp => Path.Combine(SeedingDirectory, $"AotWrapperSeeds.{TestEnvironment.PackageVersion}.stamp");

    /// <summary>
    /// Gets the list of the bitcode that AOT compilations compiled with the seeds.
    /// </summary>
    public string CompiledWithSeeds => Path.Combine(SeedingDirectory, "compiled-with-seeds.txt");

    /// <summary>
    /// Gets the aot-instances.dll.bc of the publishes' AOT compilation.
    /// </summary>
    public string PublishBitcode => Path.Combine(ProjectDirectory, "obj", "wasm", "for-publish", "aot-instances.dll.bc");

    /// <summary>
    /// Gets the aot-instances.dll.bc of the builds' AOT compilation (RunAOTCompilationAfterBuild).
    /// </summary>
    public string BuildBitcode => Path.Combine(ProjectDirectory, "obj", "wasm", "for-build", "aot-instances.dll.bc");

    /// <summary>
    /// Gets the names of the assemblies that the last AOT compilation was given.
    /// </summary>
    public IReadOnlyList<string> AotInputs => ReadMockFile("aot-inputs.txt");

    /// <summary>
    /// Gets the AOT profiles that the last AOT compilation was given.
    /// </summary>
    public IReadOnlyList<string> AotProfiles => ReadMockFile("aot-profiles.txt").SelectMany(l => l.Split(';')).ToArray();

    /// <summary>
    /// Gets WasmAotProfilePath after the last AOT compilation.
    /// </summary>
    public string ProfileAfterAot => string.Join(";", ReadMockFile("aot-profile-after.txt"));

    /// <summary>
    /// Gets a value indicating whether the AOT compilation's aot-instances.dll.bc existed when the last AOT compilation started.
    /// </summary>
    public bool BitcodeExistedBeforeAot => ReadMockFile("bitcode-existed.txt").Single() == "True";

    public string AppProfile => Path.Combine(ProjectDirectory, "aot.profile");

    /// <summary>
    /// Publishes the app with the given global properties ("Name=Value"), and the fake llvm-nm.
    /// </summary>
    public BuildResult Publish(params string[] properties) => Run("Publish", true, properties);

    /// <summary>
    /// Publishes the app like <see cref="Publish"/>, without the fake llvm-nm.
    /// </summary>
    public BuildResult PublishWithoutFakeLlvmNm(params string[] properties) => Run("Publish", false, properties);

    /// <summary>
    /// Builds the app with the given global properties ("Name=Value"), and the fake llvm-nm: it builds the native code, without AOT-compiling unless
    /// RunAOTCompilationAfterBuild is true.
    /// </summary>
    public BuildResult Build(params string[] properties) => Run("Build", true, properties);

    public BuildResult Clean() => Run("Clean", true, []);

    public void Dispose() => TestEnvironment.TryDeleteDirectory(RootDirectory);

    private BuildResult Run(string target, bool fakeLlvmNm, string[] properties)
    {
        // The targets only look for llvm-nm when _AotWrapperSeedingLlvmNm isn't set.
        string[] llvmNm = fakeLlvmNm ? [$"-p:_AotWrapperSeedingLlvmNm={TestEnvironment.FakeLlvmNm}"] : [];
        return BuildRunner.MSBuild(ProjectFile, ProjectDirectory, [$"-t:{target}", .. llvmNm, .. properties.Select(p => "-p:" + p)]);
    }

    private string[] ReadMockFile(string name)
    {
        string path = Path.Combine(ProjectDirectory, "obj", "wasm", name);
        File.Exists(path).ShouldBeTrue($"The mock AOT compilation didn't run: {name} is missing.");
        return File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
    }
}
