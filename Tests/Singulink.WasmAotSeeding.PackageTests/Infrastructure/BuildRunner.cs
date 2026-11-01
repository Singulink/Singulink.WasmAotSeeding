using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Singulink.WasmAotSeeding.PackageTests.Infrastructure;

/// <summary>
/// Runs <c>dotnet</c> builds of the test projects.
/// </summary>
internal static class BuildRunner
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs <c>dotnet msbuild</c> on the project, at normal verbosity, which shows the generator's and the checker's output, without the summary that
    /// repeats the warnings and errors.
    /// </summary>
    public static BuildResult MSBuild(string projectFile, string workingDirectory, IEnumerable<string> arguments)
    {
        string[] buildArguments = ["msbuild", projectFile, "-nologo", "-v:n", "-clp:NoSummary", "-tl:off", "-nr:false"];
        var (exitCode, output) = Run("dotnet", workingDirectory, [.. buildArguments, .. arguments]);
        return new BuildResult(exitCode, output);
    }

    public static (int ExitCode, string Output) Run(string fileName, string workingDirectory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // The test host can have MSBuild variables of its own build, which would affect these builds. Builds don't use the MSBuild server, which
        // would keep its nodes running after the tests.
        foreach (string variable in startInfo.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            startInfo.Environment.Remove(variable);

        startInfo.Environment["NUGET_PACKAGES"] = TestEnvironment.PackagesDirectory;
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        var output = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // This waits for the output to be read too, which a process that inherited the output pipes could hold up, so it has a timeout as well.
        using var timeout = new CancellationTokenSource(ProcessTimeout);

        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"'{fileName} {string.Join(' ', startInfo.ArgumentList)}' didn't finish in {ProcessTimeout}:\n{GetOutput()}");
        }

        return (process.ExitCode, GetOutput());

        void AppendLine(string? line)
        {
            if (line == null)
                return;

            lock (output)
                output.AppendLine(line);
        }

        string GetOutput()
        {
            lock (output)
                return output.ToString();
        }
    }
}
