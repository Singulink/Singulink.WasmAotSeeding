using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Singulink.WasmAotSeeding;

/// <summary>
/// Runs a command-line tool, passing on its output line by line and keeping its error output.
/// </summary>
internal static class ToolRunner
{
    // The most error output kept, enough for any message a tool prints when it fails.
    private const int MaxErrorLength = 4000;

    /// <param name="StartFailure">Why the tool couldn't start, if it couldn't.</param>
    /// <param name="TimedOut">Whether the tool was stopped because it ran longer than the timeout.</param>
    /// <param name="ExitCode">The exit code of the tool, when it finished.</param>
    /// <param name="Error">The error output of the tool.</param>
    public sealed record Result(string? StartFailure, bool TimedOut, int ExitCode, string Error);

    /// <summary>
    /// Runs <paramref name="fileName"/> and passes each line of its output to <paramref name="onOutputLine"/>, one line at a time. The tool is
    /// stopped when it runs longer than <paramref name="timeout"/> or <paramref name="cancellation"/> is canceled.
    /// </summary>
    public static Result Run(string fileName, string arguments, Action<string> onOutputLine, TimeSpan timeout, CancellationToken cancellation)
    {
        var error = new StringBuilder();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onOutputLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            lock (error)
            {
                if (e.Data != null && error.Length < MaxErrorLength)
                {
                    error.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            return new Result(e.Message, false, -1, "");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited;

        using (cancellation.Register(() => Stop(process)))
        {
            exited = process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds));

            if (!exited)
            {
                Stop(process);
            }

            // Waits for the output handlers to receive the last lines.
            process.WaitForExit();
        }

        string errorOutput;

        lock (error)
        {
            errorOutput = error.ToString().Trim();
        }

        return new Result(null, !exited, process.ExitCode, errorOutput);
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // It has already exited.
        }
    }
}
