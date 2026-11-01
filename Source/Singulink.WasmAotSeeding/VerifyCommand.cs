using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Singulink.WasmAotSeeding;

/// <summary>
/// The "verify" command, which does what ValidateAOTWrapperSeedsTask does in Uno.Wasm.Bootstrap: it checks with <c>llvm-nm</c> that the AOT
/// compilation emitted every expected interpreter entry wrapper (see <see cref="AotWrapperCheck"/>).
/// </summary>
internal static class VerifyCommand
{
    // The most AOTSEED001 errors one build logs; one more error counts the others.
    private const int MaxReportedMissingWrappers = 50;

    // llvm-nm lists the functions of a large app's bitcode in under a second.
    private static readonly TimeSpan LlvmNmTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs the command and returns its exit code (<see cref="ExitCodes"/>).
    /// </summary>
    /// <param name="options">The command's options.</param>
    /// <param name="output">Where the messages that aren't errors go: the standard output, which a normal build log (-v:n) shows.</param>
    public static int Run(Options options, TextWriter output)
    {
        var stopwatch = Stopwatch.StartNew();
        string expectedWrappersFile = options["expected-wrappers"];
        string bitcodeFile = options["bitcode"];
        string llvmNm = options["llvm-nm"];
        string docs = options["docs"];
        string issues = options["issues"];
        var messages = new BuildMessages(options["messages"]);

        int Failure(string reason)
        {
            string error = $"Couldn't check the AOT wrapper seeding of this profile-guided AOT build: {reason}. Please report this error at {issues}. " +
                $"To skip the check, set WasmAotWrapperSeedingCheck to false. See {docs}";

            messages.Error("AOTSEED002", error);
            return ExitCodes.CheckFailed;
        }

        if (bitcodeFile.Length == 0)
            return Failure("the AOT compilation didn't write aot-instances.dll.bc");

        string[] expectedWrappers;

        try
        {
            expectedWrappers = File.ReadAllLines(expectedWrappersFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failure($"couldn't read {expectedWrappersFile}: {e.Message}");
        }

        string path = Path.GetFullPath(bitcodeFile);
        var functions = new List<string>();
        var run = ToolRunner.Run(
            llvmNm,
            $"--defined-only \"{path}\"",
            line =>
            {
                if (AotWrapperCheck.ParseFunctionSymbol(line) is string function)
                    functions.Add(function);
            },
            LlvmNmTimeout,
            CancellationToken.None);

        if (run.StartFailure != null)
            return Failure($"couldn't start {llvmNm}: {run.StartFailure}");

        if (run.TimedOut)
            return Failure($"llvm-nm didn't finish reading {path} in {LlvmNmTimeout.TotalMinutes} minutes");

        if (run.ExitCode != 0)
            return Failure($"llvm-nm exited with code {run.ExitCode} reading {path}{(run.Error.Length > 0 ? ": " + run.Error : string.Empty)}");

        var result = AotWrapperCheck.Evaluate(expectedWrappers, functions);

        output.WriteLine(
            $"AOT wrapper seeding check: {result.GsharedvtInFound} of {result.GsharedvtInExpected} gsharedvt_in_sig and {result.InterpInFound} of " +
            $"{result.InterpInExpected} interp_in wrappers found in {Path.GetFileName(path)} in {stopwatch.ElapsedMilliseconds} ms.");

        foreach (var missing in result.MissingWrappers.Take(MaxReportedMissingWrappers))
        {
            string counts = missing.Expected > 1 ? $", {missing.Found} of {missing.Expected} found" : string.Empty;
            string error = $"The profile-guided AOT build lacks code that {missing.Origin} needs, so the app may crash when it reaches that method. " +
                $"Please report this error at {issues}. See {docs} (missing: {missing.Name}{counts})";

            messages.Error("AOTSEED001", error);
        }

        if (result.MissingWrappers.Count > MaxReportedMissingWrappers)
        {
            string error = $"The profile-guided AOT build lacks the code of {result.MissingWrappers.Count - MaxReportedMissingWrappers} more " +
                $"methods, which aren't listed. Please report this error at {issues}. See {docs}";

            messages.Error("AOTSEED001", error);
        }

        return result.MissingWrappers.Count == 0 ? ExitCodes.Success : ExitCodes.MissingWrappers;
    }
}
