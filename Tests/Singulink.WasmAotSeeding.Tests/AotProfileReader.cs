using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Singulink.WasmAotSeeding.Tests;

/// <summary>
/// Reads a Mono AOT profile as load_profile_file in the runtime's aot-compiler.c does (version 1.0): the "AOTPROFILE" magic, the version, then
/// records that each start with their kind and ID, until a record of kind 0. Strings are a length and UTF-8 bytes, integers little-endian. It stands
/// in for the ProfileReader of Mono.Profiler.Aot that Uno.Wasm.Bootstrap's tests read the seed profile with.
/// </summary>
internal static class AotProfileReader
{
    private const int RecordNone = 0;
    private const int RecordImage = 1;
    private const int RecordType = 2;
    private const int RecordGenericInstance = 3;
    private const int RecordMethod = 4;

    public static Profile Read(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);

        Encoding.ASCII.GetString(reader.ReadBytes(10)).ShouldBe("AOTPROFILE");
        reader.ReadInt32().ShouldBe(1 << 16, "The version isn't 1.0.");

        var modules = new Dictionary<int, Module>();
        var types = new Dictionary<int, ProfileType>();
        var methods = new List<Method>();

        while (true)
        {
            int kind = reader.ReadByte();
            int id = reader.ReadInt32();

            switch (kind)
            {
                case RecordNone:
                    reader.BaseStream.Position.ShouldBe(bytes.Length, "The profile goes on after its last record.");
                    return new Profile([.. modules.Values], [.. types.Values], methods);
                case RecordImage:
                    modules.Add(id, new Module(ReadString(reader), ReadString(reader)));
                    break;
                case RecordType:
                    reader.ReadByte().ShouldBe((byte)0x12, "The type isn't a class (MONO_TYPE_CLASS).");
                    var module = modules[reader.ReadInt32()];
                    reader.ReadInt32().ShouldBe(-1, "The type is a generic instance.");
                    types.Add(id, new ProfileType(module, ReadString(reader)));
                    break;
                case RecordMethod:
                    var type = types[reader.ReadInt32()];
                    reader.ReadInt32().ShouldBe(-1, "The method is a generic instance.");
                    int parameterCount = reader.ReadInt32();
                    methods.Add(new Method(type, ReadString(reader), ReadString(reader), parameterCount));
                    break;
                case RecordGenericInstance:
                default:
                    throw new InvalidDataException($"Unexpected record kind {kind}.");
            }
        }
    }

    private static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));

    public sealed record Profile(IReadOnlyList<Module> Modules, IReadOnlyList<ProfileType> Types, IReadOnlyList<Method> Methods);

    public sealed record Module(string Name, string Mvid);

    public sealed record ProfileType(Module Module, string Name);

    public sealed record Method(ProfileType Type, string Name, string Signature, int ParamCount);
}
