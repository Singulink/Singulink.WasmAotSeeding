using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Singulink.WasmAotSeeding.PackageTests.Infrastructure;

/// <summary>
/// Sets up the folder of this test run: the package built from this repository, extracted for the mock apps and in a local feed for restores, the
/// mock .NET WebAssembly SDK (<c>MockWasmSdk.targets</c>), and a fake llvm-nm.
/// </summary>
[TestClass]
public static class TestEnvironment
{
    /// <summary>
    /// The ID of the package built from this repository.
    /// </summary>
    public const string PackageId = "Singulink.WasmAotSeeding";

    private static readonly string BaseDirectory = Path.Combine(Path.GetTempPath(), "Singulink.WasmAotSeeding.PackageTests");

    private static string? _rootDirectory;

    /// <summary>
    /// Gets the folder that the package built from this repository is in (bin/&lt;configuration&gt; of its project).
    /// </summary>
    public static string PackageDirectory { get; } = typeof(TestEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "PackageDirectory").Value!;

    /// <summary>
    /// Gets the version of the package: the product version of the generator it was packed with, without the commit.
    /// </summary>
    public static string PackageVersion { get; } = FileVersionInfo
        .GetVersionInfo(Path.Combine(PackageDirectory, "net10.0", PackageId + ".dll")).ProductVersion!.Split('+')[0];

    /// <summary>
    /// Gets the package file, which a test of its layout reads.
    /// </summary>
    public static string PackageFile => Path.Combine(PackageDirectory, $"{PackageId}.{PackageVersion}.nupkg");

    /// <summary>
    /// Gets the folder of this test run, which the test projects are created in.
    /// </summary>
    public static string RootDirectory => _rootDirectory ?? throw new InvalidOperationException("The test environment isn't set up.");

    /// <summary>
    /// Gets the folder the package is extracted to, whose build files the mock apps import as NuGet would.
    /// </summary>
    public static string ExtractedPackageDirectory => Path.Combine(RootDirectory, "package");

    /// <summary>
    /// Gets the local feed that only has the package built from this repository.
    /// </summary>
    public static string FeedDirectory => Path.Combine(RootDirectory, "feed");

    /// <summary>
    /// Gets the NuGet global packages folder of this run (<c>NUGET_PACKAGES</c>), which keeps the package out of the user's.
    /// </summary>
    public static string PackagesDirectory => Path.Combine(RootDirectory, "packages");

    /// <summary>
    /// Gets the mock .NET WebAssembly SDK, which the mock apps import.
    /// </summary>
    public static string MockSdkFile => Path.Combine(RootDirectory, "MockWasmSdk.targets");

    /// <summary>
    /// Gets a script that stands in for llvm-nm: it lists every wrapper of the expected wrappers file that the mock AOT compilation writes as
    /// aot-instances.dll.bc, under its name and with the suffixes that the AOT compiler adds to interp_in wrappers that are taken.
    /// </summary>
    public static string FakeLlvmNm => Path.Combine(RootDirectory, "fake-llvm-nm", OperatingSystem.IsWindows() ? "llvm-nm.cmd" : "llvm-nm");

    /// <summary>
    /// Gets the core library of the runtime that runs the tests, which the mock apps AOT-compile.
    /// </summary>
    public static string CoreLibrary => typeof(object).Assembly.Location;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // Runs that were killed don't clean up after themselves.
        if (Directory.Exists(BaseDirectory))
        {
            foreach (var directory in new DirectoryInfo(BaseDirectory).GetDirectories().Where(d => d.CreationTimeUtc < DateTime.UtcNow.AddDays(-1)))
                TryDeleteDirectory(directory.FullName);
        }

        _rootDirectory = Path.Combine(BaseDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(FeedDirectory);

        ExtractPackage();
        File.Copy(PackageFile, Path.Combine(FeedDirectory, Path.GetFileName(PackageFile)));

        File.WriteAllText(MockSdkFile, MockSdk);
        WriteFakeLlvmNm();

        // Keeps the test projects from using files in folders above them, and turns off warnings that depend on the SDK or the network rather than on
        // the test. The restored project doesn't need the ASP.NET Core and Windows Desktop reference packs either, which saves downloads.
        string buildProps = """
            <Project>
              <PropertyGroup>
                <CheckEolTargetFramework>false</CheckEolTargetFramework>
                <NuGetAudit>false</NuGetAudit>
                <DisableTransitiveFrameworkReferenceDownloads>true</DisableTransitiveFrameworkReferenceDownloads>
              </PropertyGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(RootDirectory, "Directory.Build.props"), buildProps);
        File.WriteAllText(Path.Combine(RootDirectory, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(RootDirectory, "Directory.Packages.props"), "<Project />");

        // NuGet merges this with the user's configuration, so it clears anything that could get in the way. Source mapping keeps a published package
        // with the same version from being used. nuget.org has the reference packs that the .NET SDK doesn't bring for the restored project's
        // target framework (an SDK of a later .NET, for example).
        string nugetConfig = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="signatureValidationMode" value="accept" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{FeedDirectory}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <disabledPackageSources>
                <clear />
              </disabledPackageSources>
              <packageSourceMapping>
                <clear />
                <packageSource key="local">
                  <package pattern="{PackageId}" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;

        File.WriteAllText(Path.Combine(RootDirectory, "nuget.config"), nugetConfig);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (_rootDirectory != null)
            TryDeleteDirectory(_rootDirectory);
    }

    /// <summary>
    /// Deletes a folder if it exists, retrying for a while if files are still in use (by build processes that are exiting, for example).
    /// </summary>
    public static void TryDeleteDirectory(string path)
    {
        for (int attempt = 1; Directory.Exists(path) && attempt <= 10; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    // Extracts the package as NuGet does, which gives the files the times of their zip entries as UTC times (pack writes the UTC times of the files),
    // where ZipFile.ExtractToDirectory reads them as local times: the files would be newer than the seeding's stamp for hours after they're packed.
    private static void ExtractPackage()
    {
        using var package = ZipFile.OpenRead(PackageFile);

        foreach (var entry in package.Entries.Where(e => e.Name.Length > 0))
        {
            string path = Path.GetFullPath(Path.Combine(ExtractedPackageDirectory, entry.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
            File.SetLastWriteTimeUtc(path, DateTime.SpecifyKind(entry.LastWriteTime.DateTime, DateTimeKind.Utc));
        }
    }

    // The checker runs it as "llvm-nm --defined-only <bitcode>", and reads the function symbols ("<address> t <name>") of its output.
    private static void WriteFakeLlvmNm()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FakeLlvmNm)!);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(FakeLlvmNm, """
                @echo off
                for /f "usebackq tokens=1" %%a in ("%~2") do (
                  echo -------- t aot_instances_%%a
                  for /l %%i in (0,1,9) do echo -------- t aot_instances_%%a_%%i
                )
                exit /b 0

                """.ReplaceLineEndings("\r\n"));

            return;
        }

        File.WriteAllText(FakeLlvmNm, """
            #!/bin/sh
            exec awk -F '\t' 'NF { print "-------- t aot_instances_" $1; for (i = 0; i < 10; i++) print "-------- t aot_instances_" $1 "_" i }' "$2"

            """.ReplaceLineEndings("\n"));

        File.SetUnixFileMode(FakeLlvmNm, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// The parts of the .NET WebAssembly SDK (WasmApp.Common.targets and BrowserWasmApp.targets) that the package depends on, with what an AOT
    /// compilation does to them. Build and Publish both build the native code (WasmBuildNative, as Release builds of Uno.Sdk's apps do with
    /// InterpreterAndAOT), which reaches _WasmAotCompileApp, and only AOT-compile (_WasmShouldAOT) in the nested build of WasmNestedPublishApp
    /// (WasmBuildingForNestedPublish, here in the same build), or in a build when RunAOTCompilationAfterBuild is true. _WasmAotCompileApp writes
    /// aot-instances.dll.bc into _BitcodeFile, in wasm/for-publish or wasm/for-build. The mock compilation records its inputs and profiles in
    /// obj/wasm/, and writes the expected wrappers of the seed assembly among its inputs as the bitcode, which the fake llvm-nm lists, or an empty
    /// file when MockMissingWrappers is true.
    /// </summary>
    private static readonly string MockSdk = """
        <Project>
          <PropertyGroup>
            <WasmDedup Condition="'$(WasmDedup)' == ''">true</WasmDedup>
            <WasmBuildNative Condition="'$(WasmBuildNative)' == ''">true</WasmBuildNative>
            <_MockWasmDir>$(MSBuildProjectDirectory)\obj\wasm\</_MockWasmDir>
          </PropertyGroup>

          <ItemGroup>
            <_WasmAssembliesInternal Include="$(MockAotInputs);$(MockExtraAotInputs)" />
          </ItemGroup>

          <Target Name="Build" DependsOnTargets="_MockWasmBuildNative" />
          <Target Name="Clean" />
          <Target Name="Publish" DependsOnTargets="WasmNestedPublishApp" />
          <Target Name="WasmNestedPublishApp" DependsOnTargets="_MockNestedPublish;_MockWasmBuildNative" />

          <Target Name="_MockNestedPublish">
            <PropertyGroup>
              <WasmBuildingForNestedPublish>true</WasmBuildingForNestedPublish>
            </PropertyGroup>
          </Target>

          <Target Name="_MockWasmBuildNative" DependsOnTargets="_MockInitializeAot;_WasmAotCompileApp" Condition="'$(WasmBuildNative)' == 'true'" />

          <Target Name="_MockInitializeAot">
            <PropertyGroup>
              <_MockAot>false</_MockAot>
              <_MockAot Condition="'$(WasmBuildingForNestedPublish)' == 'true' or '$(RunAOTCompilationAfterBuild)' == 'true'">$(RunAOTCompilation)</_MockAot>
              <_WasmShouldAOT Condition="'$(_WasmShouldAOT)' == ''">$(_MockAot)</_WasmShouldAOT>
              <_MockBitcode>$(_MockWasmDir)for-build\aot-instances.dll.bc</_MockBitcode>
              <_MockBitcode Condition="'$(WasmBuildingForNestedPublish)' == 'true'">$(_MockWasmDir)for-publish\aot-instances.dll.bc</_MockBitcode>
            </PropertyGroup>
          </Target>

          <Target Name="_WasmAotCompileApp" Condition="'$(_WasmShouldAOT)' == 'true'">
            <ItemGroup>
              <_MockSeedAssembly Include="@(_WasmAssembliesInternal)" Condition="'%(Filename)' == '$(WasmAotWrapperSeedingAssemblyName)'" />
              <_BitcodeFile Include="$(_MockBitcode)" Condition="'$(MockNoBitcode)' != 'true'" />
            </ItemGroup>

            <PropertyGroup>
              <_MockExpectedWrappers>@(_MockSeedAssembly->'%(RootDir)%(Directory)%(Filename).wrappers.txt')</_MockExpectedWrappers>
              <_MockBitcodeExisted>$([System.IO.File]::Exists('$(_MockBitcode)'))</_MockBitcodeExisted>
            </PropertyGroup>

            <MakeDir Directories="$(_MockWasmDir);$([System.IO.Path]::GetDirectoryName('$(_MockBitcode)'))" />
            <WriteLinesToFile File="$(_MockWasmDir)aot-inputs.txt" Lines="@(_WasmAssembliesInternal->'%(Filename)')" Overwrite="true" />
            <WriteLinesToFile File="$(_MockWasmDir)aot-profiles.txt" Lines="$(WasmAotProfilePath)" Overwrite="true" />
            <WriteLinesToFile File="$(_MockWasmDir)bitcode-existed.txt" Lines="$(_MockBitcodeExisted)" Overwrite="true" />
            <Copy SourceFiles="$(_MockExpectedWrappers)" DestinationFiles="$(_MockBitcode)"
                  Condition="'$(_MockExpectedWrappers)' != '' and '$(MockMissingWrappers)' != 'true'" />
            <WriteLinesToFile File="$(_MockBitcode)" Lines="" Overwrite="true"
                              Condition="'$(_MockExpectedWrappers)' == '' or '$(MockMissingWrappers)' == 'true'" />
          </Target>

          <Target Name="_MockRecordProfileAfterAot" AfterTargets="WasmNestedPublishApp">
            <MakeDir Directories="$(_MockWasmDir)" />
            <WriteLinesToFile File="$(_MockWasmDir)aot-profile-after.txt" Lines="$(WasmAotProfilePath)" Overwrite="true" />
          </Target>
        </Project>
        """;
}
