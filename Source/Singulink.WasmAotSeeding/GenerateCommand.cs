using System;
using System.IO;
using System.Linq;

namespace Singulink.WasmAotSeeding;

/// <summary>
/// The "generate" command, which does what GenerateAOTWrapperSeedsTask does in Uno.Wasm.Bootstrap: it writes the seed assembly, its AOT profile
/// and the expected wrappers (see <see cref="AotWrapperSeedGenerator"/>), and reports the methods the seeding can't protect.
/// </summary>
internal static class GenerateCommand
{
    /// <summary>
    /// Runs the command and returns its exit code (<see cref="ExitCodes"/>).
    /// </summary>
    /// <param name="options">The command's options.</param>
    /// <param name="output">Where the messages that aren't warnings go: the standard output, which a normal build log (-v:n) shows.</param>
    public static int Run(Options options, TextWriter output)
    {
        string assemblies = options["assemblies"];
        string docs = options["docs"];
        string issues = options["issues"];
        var messages = new BuildMessages(options["messages"]);
        var files = new AotWrapperSeedGenerator.SeedFiles(options["seed-assembly"], options["seed-profile"], options["expected-wrappers"]);
        AotWrapperSeedGenerator.Result result;

        try
        {
            var paths = File.ReadAllLines(assemblies).Where(l => l.Length > 0);
            result = AotWrapperSeedGenerator.Generate(paths, files);
        }
        catch (Exception e)
        {
            // The seeding only adds safety, so a failure leaves the build as it would be without it. The first sentence is also the one of the
            // targets' AOTSEED003.
            string warning = "Couldn't add the AOT wrapper seeding to this profile-guided AOT build, so the app can still crash as described in " +
                $"{docs}. Please report this warning at {issues}, with the stack trace from a normal build log (-v:n) or a binary log (-bl): " +
                $"{e.GetType().Name}: {e.Message}";

            messages.Warning("AOTSEED003", warning);
            output.WriteLine($"AOT wrapper seeding failed: {e}");
            return ExitCodes.GenerationFailed;
        }

        var collected = result.Collected;
        output.WriteLine(
            $"AOT wrapper seeding: {result.SeedCount} signatures seeded in {result.SeedMethodCount} methods, {result.GsharedvtInWrapperCount} " +
            $"gsharedvt_in_sig and {result.InterpInWrapperCount} interp_in wrappers expected, from {result.AssemblyCount} assemblies " +
            $"({collected.InterfaceMethods} interface methods, {collected.VirtualMethods} virtual methods, {collected.DelegateTargets} delegate " +
            $"targets, {collected.ConstructedTypes} generic instances; {collected.Unsupported} unsupported signatures, {result.Skipped.Count} items " +
            $"skipped, {result.Unchecked.Count} seeds unchecked, {result.UndeterminedMethods.Count} methods not checked for AOTSEED005) in " +
            $"{result.Elapsed.TotalMilliseconds:0} ms.");

        foreach (string skipped in result.Skipped)
            output.WriteLine($"AOT wrapper seeding skipped {BuildMessages.OneLine(skipped)}");

        foreach (string uncheckedSeed in result.Unchecked)
            output.WriteLine($"AOT wrapper seeding: the check doesn't look for the wrapper of {BuildMessages.OneLine(uncheckedSeed)}");

        foreach (var undetermined in result.UndeterminedMethods)
            output.WriteLine($"AOT wrapper seeding couldn't check {undetermined.Member} for AOTSEED005: {BuildMessages.OneLine(undetermined.Reason)}");

        // Compiled code that calls such a method while it's interpreted can't find its wrapper: above 8 parameters, the runtime aborts as soon as it
        // resolves the method, when the code that calls it directly first runs, or when it builds an IMT slot the method is in (only virtual instance
        // methods are), with interp_create_method_pointer_llvmonly in interp.c; below, the call fails when it's made (no_gsharedvt_in_wrapper in
        // mini-runtime.c). Listing the method in the profile doesn't make it safe: once compiled, the runtime can't find its gsharedvt_out_sig wrapper
        // either, so interpreted code that calls it aborts (init_jit_call_info in interp.c).
        foreach (var finding in result.UnenterableMethods)
        {
            string crash = finding.ParameterCount <= WrapperSignature.MaxInterpEntryArgs
                ? $"Compiled code that calls {finding.Method} can crash the app when it makes the call"
                : $"Compiled code that calls {finding.Method} can crash the app when it starts running, even if it doesn't make the call"
                    + (finding.IsVirtual ? ", and so can compiled code that calls another method of its class through an interface" : string.Empty);

            string warning = $"{crash}: {DescribeCause(finding)}. {DescribeType(finding)} Listing the method in the AOT profile only moves the crash " +
                "to the interpreted code that calls it. Remove the default value or the attribute, or pass the parameter with 'in'. If you can't change " +
                $"the method, report the warning to the library's authors. To remove the warning, add AOTSEED005 to NoWarn. See {docs}";

            messages.Warning("AOTSEED005", warning);
        }

        // One warning per struct that the runtime can't map (WrapperSignature.FindUnsharableStruct), at the root of a cycle or with too many type
        // arguments. Compiled calls to such methods that can enter the interpreter make the AOT compiler overflow its stack or assert (calls.c adds
        // their signatures to interp_in_signatures), so recording them in the profile doesn't help.
        foreach (var unprotected in result.UnprotectedStructs)
        {
            var (cause, remedy) = unprotected.Reason == WrapperSignature.UnsharableReason.CyclicLayout
                ? ($"{unprotected.Type} contains itself through a field such as a ReadOnlyMemory<T> or an ArraySegment<T> of its own type",
                    "hold its items in a class or an array")
                : ($"{unprotected.Type} has more than 15 type arguments", "give it at most 15 type arguments");

            string warning = $"The app may crash when compiled code calls a method that takes or returns {unprotected.Type}, or a struct that holds " +
                $"it, by value, such as {unprotected.Method}, or calls another method of its class through an interface: {cause}, for which the runtime " +
                "can't make the code that enters the interpreter, and neither can the AOT wrapper seeding. Recording such calls in the AOT profile " +
                $"doesn't help: the AOT compiler can fail on them, which fails the build. Pass the struct with 'in', or {remedy}. To remove the " +
                $"warning, add AOTSEED005 to NoWarn. See {docs}";

            messages.Warning("AOTSEED005", warning);
        }

        foreach (string limit in result.Limits)
        {
            string warning = "To keep large or recursive generic code from taking too long, the AOT wrapper seeding of this profile-guided AOT build " +
                $"stopped following {limit}. The app can still crash in code that uses them, as described in {docs}. To prevent the crash, run that " +
                $"code while you record the AOT profile, so that it's compiled. Please report this warning at {issues}. To remove the warning, add " +
                "AOTSEED006 to NoWarn.";

            messages.Warning("AOTSEED006", warning);
        }

        return ExitCodes.Success;
    }

    // "its parameter 2 ('m'), of type System.Numerics.Matrix4x4, has a default value"
    private static string DescribeCause(UnenterableMethods.Finding finding) => finding.TypeArgument is string typeArgument
        ? $"its {finding.Parameter}, whose type argument is the struct {typeArgument} here, has {finding.Flags}"
        : $"its {finding.Parameter}, of type {finding.Type}, has {finding.Flags}";

    // What makes the type different: the runtime maps the other structs to new types, which don't have the flags
    // (WrapperSignature.KeepsParameterAttributes).
    private static string DescribeType(UnenterableMethods.Finding finding)
    {
        string type = finding.IsFunctionPointer
            ? "The runtime passes a function pointer"
            : "Unlike other structs, the runtime passes a struct with an explicit layout or many fields";

        return $"{type} as it is between compiled and interpreted code, along with what the default value or attribute adds to the parameter, and " +
            "then can't find the code that does it.";
    }
}
