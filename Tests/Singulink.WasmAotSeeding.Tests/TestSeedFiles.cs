using System.IO;

namespace Singulink.WasmAotSeeding.Tests;

/// <summary>
/// The files of the AOT wrapper seeding, named as the build names them.
/// </summary>
internal static class TestSeedFiles
{
    public const string AssemblyName = "AotWrapperSeeds";

    public static AotWrapperSeedGenerator.SeedFiles In(string directory) => new(
        Path.Combine(directory, AssemblyName + ".dll"),
        Path.Combine(directory, AssemblyName + ".profile"),
        Path.Combine(directory, AssemblyName + ".wrappers.txt"));
}
