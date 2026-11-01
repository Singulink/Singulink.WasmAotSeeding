using System.Runtime.InteropServices;

namespace Singulink.WasmAotSeeding.Tests;

// The methods with struct parameters that keep their flags, which the generator tests (GeneratorTests) find unenterable, and others that don't.
// With explicit layout, the runtime doesn't map it to a Mono.ValueTuple, so a parameter of this type keeps its flags.
[StructLayout(LayoutKind.Explicit)]
public struct AotSeedingExplicitBox
{
    [FieldOffset(0)]
    public int Value;
}

public static class AotSeedingAttributedParameters
{
    public static void WithDefault(int a, int b, int c, int d, int e, int f, int g, int h, AotSeedingExplicitBox box = default)
    {
    }

    // Passed by reference: the runtime replaces the type.
    public static void WithIn(int a, int b, int c, int d, int e, int f, int g, int h, in AotSeedingExplicitBox box)
    {
    }

    // 8 parameters or fewer enter the interpreter through gsharedvt_in_sig wrappers, which have the same problem, as the test app's variants showed:
    // called from compiled code, a method of 8 parameters like this one stops the app with "function signature mismatch".
    public static void WithEightParameters(int a, int b, int c, int d, int e, int f, int g, AotSeedingExplicitBox box = default)
    {
    }

    public static void WithFewParameters(int a, AotSeedingExplicitBox box = default)
    {
    }

    // Without a default value, the parameter has no flags: the same method runs.
    public static void WithoutDefault(int a, int b, int c, int d, int e, int f, int g, AotSeedingExplicitBox box)
    {
    }

    public static void WithMarshalAs(int a, [MarshalAs(UnmanagedType.Struct)] AotSeedingExplicitBox box)
    {
    }

    public static void WithInAndOut(int a, [In, Out] AotSeedingExplicitBox box)
    {
    }

    // The flags of a return value don't matter: the wrapper caches only hash the return type's element type, and the test app's variants with
    // these methods run.
    [return: MarshalAs(UnmanagedType.Struct)]
    public static AotSeedingExplicitBox WithMarshaledReturn(int a) => default;

    [return: MarshalAs(UnmanagedType.Struct)]
    public static AotSeedingExplicitBox WithMarshaledReturnAndNineParameters(int a, int b, int c, int d, int e, int f, int g, int h, int i) => default;

    // The runtime maps AotSeedingKey to a Mono.ValueTuple.
    public static void WithMappedType(int a, int b, int c, int d, int e, int f, int g, int h, AotSeedingKey key = default)
    {
    }
}
