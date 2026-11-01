using System;
using System.IO;
using System.Linq;
using Singulink.WasmAotSeeding.PackageTests.Infrastructure;

namespace Singulink.WasmAotSeeding.PackageTests;

/// <summary>
/// The package's build files in mock WebAssembly apps (see <see cref="MockApp"/>): when the seeding applies, how it's checked, when it steps aside,
/// and what it reports.
/// </summary>
[TestClass]
public class TargetsTests
{
    [TestMethod]
    public void When_Publishing_With_A_Profile_Then_The_Seeds_Join_The_AOT_Compilation_And_Are_Checked()
    {
        using var app = new MockApp("seeded");

        var result = app.Publish();

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.GenerationLine.ShouldNotBeNull(result.Output).ShouldContain(" signatures seeded in ", Case.Sensitive);
        result.CheckLine.ShouldNotBeNull(result.Output).ShouldStartWith("AOT wrapper seeding check: ", Case.Sensitive);
        result.CheckLine.ShouldEndWith(" ms.", Case.Sensitive);

        app.AotInputs.ShouldBe(["System.Private.CoreLib", "AotWrapperSeeds"]);
        app.AotProfiles.ShouldBe([app.AppProfile, Path.Combine(app.SeedingDirectory, "AotWrapperSeeds.profile")]);
        app.ProfileAfterAot.ShouldBe(app.AppProfile);
        File.Exists(app.Stamp).ShouldBeTrue("The stamp isn't named after the seed assembly and the package version.");
    }

    // Each count of the check line: "N of N gsharedvt_in_sig and M of M interp_in wrappers found".
    [TestMethod]
    public void When_Every_Wrapper_Is_Compiled_Then_The_Check_Finds_Them_All()
    {
        using var app = new MockApp("checked");

        var result = app.Publish();
        string[] words = result.CheckLine.ShouldNotBeNull(result.Output).Split(' ');

        words[6].ShouldBe(words[4], result.CheckLine);
        words[11].ShouldBe(words[9], result.CheckLine);
        int.Parse(words[4]).ShouldBeGreaterThan(0, result.CheckLine);
    }

    [TestMethod]
    public void When_Publishing_Again_Without_Changes_Then_The_Generation_Is_Skipped_And_The_Check_Runs()
    {
        using var app = new MockApp("again");
        app.Publish().ShouldSucceed();
        var stamp = File.GetLastWriteTimeUtc(app.Stamp);
        var seeds = File.GetLastWriteTimeUtc(app.SeedAssembly);

        var result = app.Publish();

        result.Succeeded.ShouldBeTrue(result.Output);
        result.GenerationLine.ShouldBeNull(result.Output);
        result.CheckLine.ShouldNotBeNull(result.Output);
        app.AotInputs.ShouldContain("AotWrapperSeeds");
        File.GetLastWriteTimeUtc(app.Stamp).ShouldBe(stamp);
        File.GetLastWriteTimeUtc(app.SeedAssembly).ShouldBe(seeds);
    }

    // A seed file that's gone generates them all again; the generator writes only the files whose content changes, so the others keep their dates.
    [TestMethod]
    public void When_A_Seed_File_Is_Gone_Then_The_Seeds_Are_Generated_Again()
    {
        using var app = new MockApp("gone");
        app.Publish().ShouldSucceed();
        var seeds = File.GetLastWriteTimeUtc(app.SeedAssembly);
        File.Delete(Path.Combine(app.SeedingDirectory, "AotWrapperSeeds.wrappers.txt"));

        var result = app.Publish();

        result.Succeeded.ShouldBeTrue(result.Output);
        result.GenerationLine.ShouldNotBeNull(result.Output);
        File.Exists(Path.Combine(app.SeedingDirectory, "AotWrapperSeeds.wrappers.txt")).ShouldBeTrue();
        File.GetLastWriteTimeUtc(app.SeedAssembly).ShouldBe(seeds);
    }

    [TestMethod]
    public void When_The_Seed_Assembly_Has_Another_Name_Then_The_Files_And_The_Stamp_Have_It()
    {
        using var app = new MockApp("named");

        var result = app.Publish("WasmAotWrapperSeedingAssemblyName=MySeeds");

        result.Succeeded.ShouldBeTrue(result.Output);
        app.AotInputs.ShouldBe(["System.Private.CoreLib", "MySeeds"]);
        File.Exists(Path.Combine(app.SeedingDirectory, $"MySeeds.{TestEnvironment.PackageVersion}.stamp")).ShouldBeTrue();
        File.Exists(Path.Combine(app.SeedingDirectory, "MySeeds.wrappers.txt")).ShouldBeTrue();
    }

    // The bitcode that the AOT compilation compiled with the seeds is deleted, so that it compiles it again without them.
    [TestMethod]
    public void When_Deduplication_Is_Off_Then_AOTSEED004_Is_Reported_And_The_Seeds_Are_Forgotten()
    {
        using var app = new MockApp("dedup");
        app.Publish().ShouldSucceed();

        var result = app.Publish("WasmDedup=false");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBe(["warning AOTSEED004"], result.Output);
        result.WithCode("AOTSEED004").Single().Message.ShouldContain(
            "Turn deduplication back on, or set WasmAotWrapperSeeding to false to remove the warning.", Case.Sensitive);
        result.GenerationLine.ShouldBeNull(result.Output);
        result.CheckLine.ShouldBeNull(result.Output);
        app.AotInputs.ShouldBe(["System.Private.CoreLib"]);
        app.AotProfiles.ShouldBe([app.AppProfile]);
        app.BitcodeExistedBeforeAot.ShouldBeFalse("The bitcode compiled with the seeds wasn't deleted.");
    }

    [TestMethod]
    public void When_The_Seeding_Is_Turned_Off_Then_Nothing_Is_Seeded_Or_Reported()
    {
        using var app = new MockApp("off");
        app.Publish().ShouldSucceed();

        var result = app.Publish("WasmAotWrapperSeeding=false");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.SeedingLines.ShouldBeEmpty(result.Output);
        app.AotInputs.ShouldBe(["System.Private.CoreLib"]);
        app.BitcodeExistedBeforeAot.ShouldBeFalse("The bitcode compiled with the seeds wasn't deleted.");
    }

    [TestMethod]
    [DataRow("WasmShellEnableAOTWrapperSeeding=true", DisplayName = "The bootstrapper's property")]
    [DataRow("_UnoAOTWrapperSeedingRan=true", DisplayName = "The bootstrapper's seeding ran first")]
    public void When_The_Bootstrapper_Has_Its_Own_Seeding_Then_AOTSEED007_Is_Reported_And_Nothing_Is_Seeded(string property)
    {
        using var app = new MockApp("official");

        var result = app.Publish(property);

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBe(["warning AOTSEED007"], result.Output);
        result.WithCode("AOTSEED007").Single().Message.ShouldContain(
            "so the Singulink.WasmAotSeeding package is off: remove its PackageReference (see \"Removing It\" in its README).", Case.Sensitive);
        result.SeedingLines.ShouldBeEmpty(result.Output);
        app.AotInputs.ShouldBe(["System.Private.CoreLib"]);
    }

    [TestMethod]
    public void When_The_Bootstrapper_Seeds_Are_Among_The_AOT_Inputs_Then_AOTSEED007_Is_Reported()
    {
        using var app = new MockApp("official-seeds");
        string officialSeeds = Path.Combine(app.RootDirectory, "Uno.Wasm.AotWrapperSeeds.dll");
        File.WriteAllText(officialSeeds, string.Empty);

        var result = app.Publish($"MockExtraAotInputs={officialSeeds}");

        result.Codes.ShouldBe(["warning AOTSEED007"], result.Output);
        app.AotInputs.ShouldBe(["System.Private.CoreLib", "Uno.Wasm.AotWrapperSeeds"]);
    }

    [TestMethod]
    public void When_Wrappers_Are_Missing_Then_AOTSEED001_Fails_The_Build()
    {
        using var app = new MockApp("missing");

        var result = app.Publish("MockMissingWrappers=true");

        result.Succeeded.ShouldBeFalse(result.Output);
        var errors = result.WithCode("AOTSEED001");
        errors.Length.ShouldBeGreaterThan(2, result.Output);
        errors.ShouldAllBe(e => e.Kind == "error");
        errors[0].Message.ShouldContain("needs, so the app may crash when it reaches that method.", Case.Sensitive);
        errors[^1].Message.ShouldStartWith("The AOT wrapper seeding check failed: the build lacks the code that the errors above name.", Case.Sensitive);
        result.Codes.Distinct().ShouldBe(["error AOTSEED001"], result.Output);
    }

    [TestMethod]
    public void When_The_Check_Is_Turned_Off_Then_Missing_Wrappers_Pass()
    {
        using var app = new MockApp("unchecked");

        var result = app.Publish("MockMissingWrappers=true", "WasmAotWrapperSeedingCheck=false");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.GenerationLine.ShouldNotBeNull(result.Output);
        result.CheckLine.ShouldBeNull(result.Output);
        app.AotInputs.ShouldContain("AotWrapperSeeds");
    }

    // The checker looks for llvm-nm in EmscriptenUpstreamBinPath, here a folder without it.
    [TestMethod]
    public void When_LlvmNm_Is_Missing_Then_AOTSEED002_Fails_The_Build()
    {
        using var app = new MockApp("no-llvm-nm");
        string bin = Path.Combine(app.RootDirectory, "emscripten bin");
        Directory.CreateDirectory(bin);

        var result = app.PublishWithoutFakeLlvmNm($"EmscriptenUpstreamBinPath={bin}");

        result.Succeeded.ShouldBeFalse(result.Output);
        result.Codes.ShouldBe(["error AOTSEED002"], result.Output);
        string llvmNm = Path.Combine(bin, OperatingSystem.IsWindows() ? "llvm-nm.exe" : "llvm-nm");
        result.WithCode("AOTSEED002").Single().Message.ShouldContain($"couldn't start {llvmNm}: ", Case.Sensitive);
    }

    [TestMethod]
    public void When_The_AOT_Compilation_Writes_No_Bitcode_Then_AOTSEED002_Fails_The_Build()
    {
        using var app = new MockApp("no-bitcode");

        var result = app.Publish("MockNoBitcode=true");

        result.Succeeded.ShouldBeFalse(result.Output);
        result.Codes.ShouldBe(["error AOTSEED002"], result.Output);
        result.WithCode("AOTSEED002").Single().Message.ShouldContain("the AOT compilation didn't write aot-instances.dll.bc.", Case.Sensitive);
    }

    // Without the core library among its inputs, the generator fails: the build goes on without the seeding, and without the files of the earlier
    // publish, which the stamp would otherwise apply.
    [TestMethod]
    public void When_The_Generation_Fails_Then_AOTSEED003_Is_Reported_And_The_Build_Goes_On_Without_The_Seeds()
    {
        using var app = new MockApp("failed");
        app.Publish().ShouldSucceed();
        string input = Path.Combine(TestEnvironment.ExtractedPackageDirectory, "tools", "net10.0", "Mono.Cecil.dll");

        var result = app.Publish($"MockAotInputs={input}");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBe(["warning AOTSEED003"], result.Output);
        result.WithCode("AOTSEED003").Single().Message.ShouldContain("System.Private.CoreLib is not among the AOT inputs.", Case.Sensitive);
        result.Output.ShouldContain("AOT wrapper seeding failed: System.InvalidOperationException: ", Case.Sensitive);
        app.AotInputs.ShouldBe(["Mono.Cecil"]);
        app.BitcodeExistedBeforeAot.ShouldBeFalse("The bitcode compiled with the seeds wasn't deleted.");
        File.Exists(app.Stamp).ShouldBeFalse();
        File.Exists(app.SeedAssembly).ShouldBeFalse();
    }

    // dotnet exec can't run an assembly that isn't an app: the generator didn't run, which fails the build.
    [TestMethod]
    public void When_The_Generator_Cannot_Run_Then_AOTSEED008_Fails_The_Build()
    {
        using var app = new MockApp("not-run");
        string notAnApp = Path.Combine(TestEnvironment.ExtractedPackageDirectory, "tools", "net10.0", "Mono.Cecil.dll");

        var result = app.Publish($"_AotWrapperSeedingTool={notAnApp}");

        result.Succeeded.ShouldBeFalse(result.Output);
        result.Codes.ShouldBe(["error AOTSEED008"], result.Output);
        string message = result.WithCode("AOTSEED008").Single().Message;
        message.ShouldStartWith("The AOT wrapper seeding generator didn't run or didn't finish: \"", Case.Sensitive);
        message.ShouldContain("\" exec exited with code ", Case.Sensitive);
        message.ShouldContain($" for {notAnApp}. The messages above", Case.Sensitive);
        message.ShouldContain("such as that dotnet wasn't found or found no .NET 10 or later runtime", Case.Sensitive);
        File.Exists(app.Stamp).ShouldBeFalse();
    }

    // An SDK whose targets have other names doesn't run the seeding, which WasmNestedPublishApp reports.
    [TestMethod]
    public void When_The_SDK_Does_Not_Run_The_Hooked_Targets_Then_AOTSEED003_Is_Reported()
    {
        using var app = new MockApp("not-hooked");

        var result = app.Publish("_WasmShouldAOT=false");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBe(["warning AOTSEED003"], result.Output);
        result.WithCode("AOTSEED003").Single().Message.ShouldContain(
            "The .NET SDK didn't run the targets that the seeding hooks (_WasmAotCompileApp, with _WasmShouldAOT true)", Case.Sensitive);
    }

    [TestMethod]
    [DataRow("WasmAotProfilePath=", DisplayName = "Without an AOT profile")]
    [DataRow("RunAOTCompilation=false", DisplayName = "Without AOT compilation")]
    [DataRow("UseMonoRuntime=false", DisplayName = "On CoreCLR")]
    public void When_The_Build_Is_Not_A_Profile_Guided_Mono_AOT_Build_Then_Nothing_Is_Seeded_Or_Reported(string property)
    {
        using var app = new MockApp("not-profile-guided");

        var result = app.Publish(property);

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.SeedingLines.ShouldBeEmpty(result.Output);
        Directory.Exists(app.SeedingDirectory).ShouldBeFalse();
    }

    // The package does nothing in the other target frameworks of a project, which have neither the browserwasm platform nor the browser-wasm
    // runtime identifier.
    [TestMethod]
    [DataRow("net10.0", DisplayName = "No platform")]
    [DataRow("net10.0-desktop", DisplayName = "Another platform")]
    [DataRow("net10.0-browser", DisplayName = "The browser platform")]
    public void When_The_Project_Does_Not_Build_For_WebAssembly_Then_Nothing_Is_Seeded_Or_Reported(string targetFramework)
    {
        using var app = new MockApp("platform");

        var result = app.Publish($"TargetFramework={targetFramework}");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.SeedingLines.ShouldBeEmpty(result.Output);
        Directory.Exists(app.SeedingDirectory).ShouldBeFalse();
    }

    [TestMethod]
    public void When_The_Target_Platform_Is_Browserwasm_On_A_Later_NET_Then_The_Seeding_Applies()
    {
        using var app = new MockApp("later");

        var result = app.Publish("TargetFramework=net11.0-browserwasm");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.GenerationLine.ShouldNotBeNull(result.Output);
        result.CheckLine.ShouldNotBeNull(result.Output);
    }

    // Projects of the .NET WebAssembly SDK, Microsoft.NET.Sdk.WebAssembly's and Blazor's, target plain net10.0 with this runtime identifier.
    [TestMethod]
    public void When_The_Runtime_Identifier_Is_Browser_Wasm_Then_The_Seeding_Applies()
    {
        using var app = new MockApp("rid");

        var result = app.Publish("TargetFramework=net10.0", "RuntimeIdentifier=browser-wasm");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Codes.ShouldBeEmpty(result.Output);
        result.GenerationLine.ShouldNotBeNull(result.Output);
        result.CheckLine.ShouldNotBeNull(result.Output);
        app.AotInputs.ShouldContain("AotWrapperSeeds");
    }

    // A Release build of an app that builds its native code reaches _WasmAotCompileApp without AOT-compiling: it must leave the bitcode that the last
    // publish compiled with the seeds, and the seeding's files, as they are, so that the next publish doesn't compile everything again.
    [TestMethod]
    public void When_A_Build_Does_Not_AOT_Compile_Then_The_Code_Compiled_With_The_Seeds_Stays()
    {
        using var app = new MockApp("build-between");
        app.Publish().ShouldSucceed();
        var bitcode = File.GetLastWriteTimeUtc(app.PublishBitcode);
        var stamp = File.GetLastWriteTimeUtc(app.Stamp);

        var build = app.Build();

        build.Succeeded.ShouldBeTrue(build.Output);
        build.Codes.ShouldBeEmpty(build.Output);
        build.SeedingLines.ShouldBeEmpty(build.Output);
        File.Exists(app.PublishBitcode).ShouldBeTrue("The build deleted the bitcode that the publish compiled with the seeds.");
        File.GetLastWriteTimeUtc(app.PublishBitcode).ShouldBe(bitcode);
        File.Exists(app.CompiledWithSeeds).ShouldBeTrue();
        File.GetLastWriteTimeUtc(app.Stamp).ShouldBe(stamp);

        var publish = app.Publish();

        publish.Succeeded.ShouldBeTrue(publish.Output);
        publish.GenerationLine.ShouldBeNull(publish.Output);
        publish.CheckLine.ShouldNotBeNull(publish.Output);
        app.BitcodeExistedBeforeAot.ShouldBeTrue();
    }

    // A build that AOT-compiles (RunAOTCompilationAfterBuild) compiles into another folder than a publish: when the seeding stops applying, the
    // bitcode of both goes.
    [TestMethod]
    public void When_Builds_And_Publishes_Were_Seeded_Then_The_Bitcode_Of_Both_Is_Forgotten()
    {
        using var app = new MockApp("both");
        app.Publish().ShouldSucceed();
        app.Build("RunAOTCompilationAfterBuild=true").ShouldSucceed().CheckLine.ShouldNotBeNull();
        File.ReadAllLines(app.CompiledWithSeeds).Order(StringComparer.Ordinal).ShouldBe([app.BuildBitcode, app.PublishBitcode]);

        var result = app.Publish("WasmAotWrapperSeeding=false");

        result.Succeeded.ShouldBeTrue(result.Output);
        app.BitcodeExistedBeforeAot.ShouldBeFalse("The bitcode that the publish compiled with the seeds wasn't deleted.");
        File.Exists(app.BuildBitcode).ShouldBeFalse("The bitcode that the build compiled with the seeds wasn't deleted.");
        File.Exists(app.CompiledWithSeeds).ShouldBeFalse();
    }

    // Going back to that version would otherwise apply the files of this one: those of the other version are older than its stamp. The stamps of
    // another seed assembly stay.
    [TestMethod]
    public void When_The_Seeds_Are_Generated_Then_The_Stamps_Of_Other_Versions_Go()
    {
        using var app = new MockApp("stamps");
        app.Publish().ShouldSucceed();
        string otherVersion = Path.Combine(app.SeedingDirectory, "AotWrapperSeeds.0.9.0.stamp");
        string otherAssembly = Path.Combine(app.SeedingDirectory, $"MySeeds.{TestEnvironment.PackageVersion}.stamp");
        File.WriteAllText(otherVersion, string.Empty);
        File.WriteAllText(otherAssembly, string.Empty);
        File.Delete(Path.Combine(app.SeedingDirectory, "AotWrapperSeeds.wrappers.txt"));

        var result = app.Publish();

        result.GenerationLine.ShouldNotBeNull(result.Output);
        File.Exists(app.Stamp).ShouldBeTrue();
        File.Exists(otherVersion).ShouldBeFalse();
        File.Exists(otherAssembly).ShouldBeTrue();
    }

    // Visual Studio and Rider don't set DOTNET_HOST_PATH: the generator runs with the dotnet in the SDK's NetCoreRoot, and else with the one on the
    // PATH. The mock apps have no SDK, so no NetCoreRoot unless the test sets it.
    [TestMethod]
    public void When_DOTNET_HOST_PATH_Is_Not_Set_Then_The_Dotnet_On_The_PATH_Runs_The_Generator()
    {
        using var app = new MockApp("no-host-path");

        var result = app.Publish("DOTNET_HOST_PATH=");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Output.ShouldContain("\"dotnet\" exec \"", Case.Sensitive);
        result.GenerationLine.ShouldNotBeNull(result.Output);
        result.CheckLine.ShouldNotBeNull(result.Output);
    }

    [TestMethod]
    public void When_DOTNET_HOST_PATH_Is_Not_Set_Then_The_Dotnet_In_NetCoreRoot_Runs_The_Generator()
    {
        using var app = new MockApp("net-core-root");

        // The folder of the dotnet that runs the tests, without a trailing separator.
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(TestEnvironment.CoreLibrary)!, "..", "..", ".."));
        string dotnet = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        var result = app.Publish("DOTNET_HOST_PATH=", $"NetCoreRoot={root}");

        result.Succeeded.ShouldBeTrue(result.Output);
        result.Output.ShouldContain($"\"{dotnet}\" exec \"", Case.Sensitive);
        result.GenerationLine.ShouldNotBeNull(result.Output);
    }

    [TestMethod]
    public void When_Cleaning_Then_The_Seeding_Files_Are_Removed()
    {
        using var app = new MockApp("clean");
        app.Publish().ShouldSucceed();

        var result = app.Clean();

        result.Succeeded.ShouldBeTrue(result.Output);
        Directory.Exists(app.SeedingDirectory).ShouldBeFalse();
    }
}
