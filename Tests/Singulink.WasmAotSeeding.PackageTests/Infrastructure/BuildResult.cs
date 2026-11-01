using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Singulink.WasmAotSeeding.PackageTests.Infrastructure;

/// <summary>
/// The result of building a test project.
/// </summary>
internal sealed partial class BuildResult
{
    public BuildResult(int exitCode, string output)
    {
        ExitCode = exitCode;
        Output = output;
        Diagnostics = output.Split('\n')
            .Select(l => DiagnosticRegex().Match(l.TrimEnd('\r')))
            .Where(m => m.Success)
            .Select(m => new Diagnostic(m.Groups["kind"].Value, m.Groups["code"].Value, m.Groups["message"].Value))
            .ToList();
    }

    public int ExitCode { get; }

    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Gets the build output, for assertion messages.
    /// </summary>
    public string Output { get; }

    /// <summary>
    /// Gets the warnings and errors, in the order they were reported.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets the warnings and errors of the package (AOTSEED codes) and of the build in general (MSBuild, NuGet and SDK codes) as "kind code", e.g.
    /// "warning AOTSEED004".
    /// </summary>
    public string[] Codes => Diagnostics.Select(d => $"{d.Kind} {d.Code}").ToArray();

    /// <summary>
    /// Gets the output lines of the generator and the checker that aren't warnings or errors.
    /// </summary>
    public string[] SeedingLines => Output.Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("AOT wrapper seeding", StringComparison.Ordinal))
        .ToArray();

    /// <summary>
    /// Gets the line that the generator writes when it ran, or <see langword="null"/>.
    /// </summary>
    public string? GenerationLine => SeedingLines.SingleOrDefault(l => l.StartsWith("AOT wrapper seeding: ", StringComparison.Ordinal));

    /// <summary>
    /// Gets the line that the checker writes when it ran, or <see langword="null"/>.
    /// </summary>
    public string? CheckLine => SeedingLines.SingleOrDefault(l => l.StartsWith("AOT wrapper seeding check: ", StringComparison.Ordinal));

    /// <summary>
    /// Gets the warnings or errors with the given code.
    /// </summary>
    public Diagnostic[] WithCode(string code) => Diagnostics.Where(d => d.Code == code).ToArray();

    /// <summary>
    /// Fails the test with the build's output if the build failed.
    /// </summary>
    public BuildResult ShouldSucceed()
    {
        Succeeded.ShouldBeTrue(Output);
        return this;
    }

    public override string ToString() => Output;

    // E.g. "C:\App\Singulink.WasmAotSeeding.targets(120,5): warning AOTSEED004: Message [C:\App\App.proj]".
    [GeneratedRegex(
        @"^(?<file>.*?)(?:\((?<line>\d+),(?<column>\d+)\))?\s*:\s*(?<kind>warning|error)\s*(?<code>[A-Za-z]+\d+)?\s*:\s*(?<message>.*?)(?:\s+\[[^\[\]]*\])?$")]
    private static partial Regex DiagnosticRegex();
}

/// <summary>
/// A warning or error from the build output.
/// </summary>
internal sealed record Diagnostic(string Kind, string Code, string Message);
