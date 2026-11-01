using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Singulink.WasmAotSeeding.Tests;

/// <summary>
/// Runs the generator's commands in the test process, as the targets run them in a process of their own, and collects what they report.
/// </summary>
internal static class Commands
{
    public const string DocsUrl = "https://docs.example";

    public const string IssuesUrl = "https://issues.example";

    /// <summary>
    /// Runs "generate" over the assemblies, writing the files. The list of assemblies and the messages go to a folder of the run, in the seed
    /// assembly's folder.
    /// </summary>
    public static CommandRun Generate(AotWrapperSeedGenerator.SeedFiles files, params string[] assemblies)
    {
        string runDirectory = CreateRunDirectory(Path.GetDirectoryName(Path.GetFullPath(files.Assembly))!);
        string list = Path.Combine(runDirectory, "assemblies.txt");
        File.WriteAllLines(list, assemblies);

        return Run(runDirectory, (messages, output) => GenerateCommand.Run(
            new Options([
                "generate",
                "--assemblies", list,
                "--seed-assembly", files.Assembly,
                "--seed-profile", files.Profile,
                "--expected-wrappers", files.ExpectedWrappers,
                "--messages", messages,
                "--docs", DocsUrl,
                "--issues", IssuesUrl,
            ]),
            output));
    }

    /// <summary>
    /// Runs "verify" with the given llvm-nm. The messages go to a folder of the run, in the expected wrappers' folder.
    /// </summary>
    public static CommandRun Verify(string expectedWrappers, string bitcode, string llvmNm)
    {
        string runDirectory = CreateRunDirectory(Path.GetDirectoryName(Path.GetFullPath(expectedWrappers))!);

        return Run(runDirectory, (messages, output) => VerifyCommand.Run(
            new Options([
                "verify",
                "--expected-wrappers", expectedWrappers,
                "--bitcode", bitcode,
                "--llvm-nm", llvmNm,
                "--messages", messages,
                "--docs", DocsUrl,
                "--issues", IssuesUrl,
            ]),
            output));
    }

    private static string CreateRunDirectory(string parent)
    {
        string directory = Path.Combine(parent, "run-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // The commands write their other messages to the writer they're given, which is the standard output when the targets run them.
    private static CommandRun Run(string runDirectory, Func<string, TextWriter, int> run)
    {
        string messages = Path.Combine(runDirectory, "messages");
        var output = new StringWriter();
        int exitCode = run(messages, output);

        var diagnostics = Directory.Exists(messages)
            ? Directory.GetFiles(messages, "*.txt")
                .Order(StringComparer.Ordinal)
                .SelectMany(f => File.ReadAllLines(f).Select(l => new Diagnostic(Path.GetFileNameWithoutExtension(f), l)))
                .ToList()
            : [];

        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        return new CommandRun(exitCode, lines, diagnostics);
    }
}

/// <summary>
/// What a command reported: its exit code (<see cref="ExitCodes"/>), its other messages, one per line, and the warnings or errors it wrote to the
/// messages folder, in the order of their codes.
/// </summary>
internal sealed record CommandRun(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// A warning or an error that a command wrote to the messages folder.
/// </summary>
internal sealed record Diagnostic(string Code, string Message);
