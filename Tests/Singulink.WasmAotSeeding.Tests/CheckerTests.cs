using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Singulink.WasmAotSeeding.Tests;

/// <summary>
/// The "verify" command and the tool runner it starts llvm-nm with.
/// </summary>
[TestClass]
public class CheckerTests
{
    private static string _directory = string.Empty;
    private static string _expectedWrappers = string.Empty;

    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        _directory = Path.Combine(Path.GetTempPath(), "wasm-aot-seeding-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _expectedWrappers = Path.Combine(_directory, "expected.txt");
        File.WriteAllText(_expectedWrappers, string.Empty);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The dotnet host that runs the tests, a tool that exists on every machine they run on.
    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

    private static string Bitcode => Path.Combine(_directory, "aot-instances.dll.bc");

    [TestMethod]
    public void When_LlvmNm_Is_Missing_Then_AOTSEED002_Is_Logged()
    {
        var run = Commands.Verify(_expectedWrappers, Bitcode, Path.Combine(_directory, "missing", "llvm-nm.exe"));

        run.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        var error = run.Diagnostics.ShouldHaveSingleItem();
        error.Code.ShouldBe("AOTSEED002");
        error.Message.ShouldContain("couldn't start", Case.Sensitive);
        error.Message.ShouldContain("Please report this error at https://issues.example.", Case.Sensitive);
        error.Message.ShouldEndWith("To skip the check, set WasmAotWrapperSeedingCheck to false. See https://docs.example", Case.Sensitive);
    }

    // dotnet --defined-only <file> fails, and writes why to its standard error, in the language of the SDK.
    [TestMethod]
    public void When_LlvmNm_Fails_Then_AOTSEED002_Has_Its_Error_Output()
    {
        var run = Commands.Verify(_expectedWrappers, Bitcode, Dotnet);

        run.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        var error = run.Diagnostics.ShouldHaveSingleItem();
        error.Code.ShouldBe("AOTSEED002");
        error.Message.ShouldMatch(Regex.Escape($"reading {Path.GetFullPath(Bitcode)}: ") + @"\S");
    }

    // The targets pass an empty path when the AOT compilation wrote no aot-instances.dll.bc.
    [TestMethod]
    public void When_The_AOT_Compilation_Wrote_No_Bitcode_Then_AOTSEED002_Is_Logged()
    {
        var run = Commands.Verify(_expectedWrappers, string.Empty, Dotnet);

        run.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        run.Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("the AOT compilation didn't write aot-instances.dll.bc.", Case.Sensitive);
    }

    // A tool that lists no function: every expected wrapper is missing, and those past the first 50 are only counted.
    [TestMethod]
    public void When_More_Than_50_Wrappers_Are_Missing_Then_The_Others_Are_Counted()
    {
        string expectedWrappers = Path.Combine(_directory, "many.txt");
        File.WriteAllLines(expectedWrappers, Enumerable.Range(0, 60).Select(i => $"gsharedvt_in_sig_void_i{i}\tSample::Method{i} [Sample]"));

        var run = Commands.Verify(expectedWrappers, Bitcode, WriteTool("silent", []));

        run.ExitCode.ShouldBe(ExitCodes.MissingWrappers);
        run.Diagnostics.Count(e => e.Code == "AOTSEED001").ShouldBe(51, string.Join(Environment.NewLine, run.Diagnostics.Select(e => e.Message)));
        run.Diagnostics[49].Message.ShouldContain(
            "Sample::Method49 [Sample] needs, so the app may crash when it reaches that method. Please report this error at https://issues.example.",
            Case.Sensitive);
        run.Diagnostics[50].Message.ShouldContain("lacks the code of 10 more methods, which aren't listed.", Case.Sensitive);
    }

    // The functions of llvm-nm's output: a gsharedvt_in_sig wrapper, and an interp_in wrapper that two methods need, under its name and with the
    // suffix that the AOT compiler adds to the second.
    [TestMethod]
    public void When_Every_Wrapper_Is_Found_Then_The_Check_Passes()
    {
        const string InterpIn = "wrapper_other_object_interp_in_generic_long_long_long_long_long_long_long_long_long";
        string expectedWrappers = Path.Combine(_directory, "found.txt");
        File.WriteAllLines(expectedWrappers, ["gsharedvt_in_sig_i8_i8i8\tA::M [A]", InterpIn + "\tB::M [B]", InterpIn + "\tB::N [B]"]);

        string tool = WriteTool("found", [
            "-------- t aot_instances_aot_wrapper_gsharedvt_in_sig_i8_i8i8",
            "-------- d info_aot_instances_aot_wrapper_gsharedvt_in_sig_i8_i8i8",
            "-------- t aot_instances_" + InterpIn,
            "-------- t aot_instances_" + InterpIn + "_0",
        ]);

        var run = Commands.Verify(expectedWrappers, Bitcode, tool);

        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Diagnostics.ShouldBeEmpty();
        run.Output.ShouldHaveSingleItem().ShouldStartWith(
            "AOT wrapper seeding check: 1 of 1 gsharedvt_in_sig and 2 of 2 interp_in wrappers found in aot-instances.dll.bc in ", Case.Sensitive);
    }

    // A script that prints the given lines and exits, in a single process.
    private static string WriteTool(string name, string[] lines)
    {
        if (OperatingSystem.IsWindows())
        {
            string script = Path.Combine(_directory, name + ".cmd");
            File.WriteAllText(script, string.Concat(lines.Select(l => $"@echo {l}\r\n")) + "@exit /b 0\r\n");
            return script;
        }

        string shellScript = Path.Combine(_directory, name + ".sh");
        File.WriteAllText(shellScript, "#!/bin/sh\n" + string.Concat(lines.Select(l => $"echo '{l}'\n")) + "exit 0\n");
        File.SetUnixFileMode(shellScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellScript;
    }

    [TestMethod]
    public void When_The_Expected_Wrappers_Cannot_Be_Read_Then_AOTSEED002_Is_Logged()
    {
        string expectedWrappers = Path.Combine(_directory, "missing", "expected.txt");

        var run = Commands.Verify(expectedWrappers, Bitcode, Dotnet);

        run.ExitCode.ShouldBe(ExitCodes.CheckFailed);
        var error = run.Diagnostics.ShouldHaveSingleItem();
        error.Code.ShouldBe("AOTSEED002");
        error.Message.ShouldContain("couldn't read " + expectedWrappers, Case.Sensitive);
    }

    [TestMethod]
    public void When_A_Tool_Runs_Longer_Than_The_Timeout_Then_It_Is_Stopped()
    {
        var run = ToolRunner.Run(Dotnet, "--info", _ => { }, TimeSpan.FromMilliseconds(1), default);

        run.TimedOut.ShouldBeTrue();
    }

    // A tool that would run for a minute, in a single process, stopped from another thread.
    [TestMethod]
    public void When_Canceled_Then_A_Running_Tool_Is_Stopped()
    {
        var (tool, arguments) = OperatingSystem.IsWindows() ? ("powershell.exe", "-NoProfile -Command Start-Sleep 60") : ("sleep", "60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        var run = ToolRunner.Run(tool, arguments, _ => { }, TimeSpan.FromMinutes(5), cancellation.Token);

        run.StartFailure.ShouldBeNull();
        run.TimedOut.ShouldBeFalse();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), $"The tool was stopped after {stopwatch.Elapsed}.");
    }

    [TestMethod]
    public void When_A_Tool_Runs_Then_Each_Output_Line_Is_Passed_On()
    {
        int lines = 0;
        var run = ToolRunner.Run(Dotnet, "--version", _ => lines++, TimeSpan.FromMinutes(1), default);

        run.ExitCode.ShouldBe(0);
        lines.ShouldBeGreaterThan(0);
    }
}
