using System;
using System.Collections.Generic;

namespace Singulink.WasmAotSeeding;

/// <summary>
/// The command's options: <c>--name value</c> pairs after the command.
/// </summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Options(string[] args)
    {
        for (int i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new OptionsException($"Unexpected argument: {args[i]}");

            _values[args[i][2..]] = args[i + 1];
        }
    }

    public string this[string name] => _values.TryGetValue(name, out string? value) ? value : throw new OptionsException($"Missing --{name}.");
}

/// <summary>
/// The command line is wrong.
/// </summary>
internal sealed class OptionsException(string message) : Exception(message);
