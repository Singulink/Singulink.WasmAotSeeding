// The AOT wrapper seeding of profile-guided Mono WebAssembly AOT builds, which build\Singulink.WasmAotSeeding.targets runs with dotnet exec: "generate"
// before the AOT compilation and "verify" after it. They do what GenerateAOTWrapperSeedsTask and ValidateAOTWrapperSeedsTask do in Uno.Wasm.Bootstrap,
// whose seeding code the files of Upstream/ copy, with this package's AOTSEED codes. They write their warnings and errors to files of the --messages
// folder, which the targets log, and their other messages to the standard output. See README.md.

using System;
using System.Linq;
using System.Text;
using Singulink.WasmAotSeeding;

// Exec reads the output as UTF-8 (StdOutEncoding in the targets), and type names can have any characters.
Console.OutputEncoding = new UTF8Encoding(false);

try
{
    return args.FirstOrDefault() switch
    {
        "generate" => GenerateCommand.Run(new Options(args), Console.Out),
        "verify" => VerifyCommand.Run(new Options(args), Console.Out),
        _ => Usage(null),
    };
}
catch (OptionsException e)
{
    return Usage(e.Message);
}

static int Usage(string? problem)
{
    if (problem != null)
        Console.Error.WriteLine(problem);

    Console.Error.WriteLine("""
        Usage:
          dotnet exec Singulink.WasmAotSeeding.dll generate --assemblies <file listing the AOT input assemblies> --seed-assembly <dll to write>
                                                            --seed-profile <profile to write> --expected-wrappers <file to write>
                                                            --messages <folder> --docs <url> --issues <url>
          dotnet exec Singulink.WasmAotSeeding.dll verify --expected-wrappers <file> --bitcode <aot-instances.dll.bc> --llvm-nm <path>
                                                          --messages <folder> --docs <url> --issues <url>

        Warnings and errors are written to <folder>/<code>.txt, one message per line.
        """);

    return ExitCodes.UsageError;
}
