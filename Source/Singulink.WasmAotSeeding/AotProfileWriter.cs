using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Singulink.WasmAotSeeding;

/// <summary>
/// Writes a Mono AOT profile (the format read by load_profile_file in the runtime's aot-compiler.c, version 1.0) that lists non-generic methods of
/// non-generic types. The AOT compiler matches each method by its assembly name, class name, method name, parameter count and signature string.
/// For a seed profile with at least one seed method, it writes the same bytes as the ProfileWriter of Mono.Profiler.Aot, which Uno.Wasm.Bootstrap
/// uses (that one also writes the module and type records of a profile without methods).
/// </summary>
internal sealed class AotProfileWriter
{
    private const byte RecordNone = 0;
    private const byte RecordImage = 1;
    private const byte RecordType = 2;
    private const byte RecordMethod = 4;
    private const byte MonoTypeClass = 0x12;

    private readonly Dictionary<string, int> _images = [];
    private readonly Dictionary<(int Image, string Name), int> _types = [];
    private readonly MemoryStream _records = new();
    private int _nextId;

    /// <summary>
    /// Adds a method. <paramref name="className"/> is <c>Namespace.Name</c>, with <c>/</c> separating nested types; <paramref name="signature"/> is in
    /// the runtime's mono_signature_full_name format, such as <c>void()</c>.
    /// </summary>
    public void AddMethod(string assemblyName, string className, string methodName, int parameterCount, string signature)
    {
        if (!_images.TryGetValue(assemblyName, out int imageId))
        {
            imageId = _nextId++;
            _images.Add(assemblyName, imageId);
            WriteHeader(RecordImage, imageId);
            WriteString(assemblyName);
            WriteString(string.Empty); // MVID, ignored by the compiler
        }

        // The compiler splits the class name at its last dot into namespace and name, and requires one.
        if (!className.Contains('.', StringComparison.Ordinal))
            className = "." + className;

        if (!_types.TryGetValue((imageId, className), out int typeId))
        {
            typeId = _nextId++;
            _types.Add((imageId, className), typeId);
            WriteHeader(RecordType, typeId);
            _records.WriteByte(MonoTypeClass);
            WriteInt(_records, imageId);
            WriteInt(_records, -1); // no generic instance
            WriteString(className);
        }

        WriteHeader(RecordMethod, _nextId++);
        WriteInt(_records, typeId);
        WriteInt(_records, -1); // no generic instance
        WriteInt(_records, parameterCount);
        WriteString(methodName);
        WriteString(signature);
    }

    /// <summary>
    /// Returns the profile.
    /// </summary>
    public byte[] ToArray()
    {
        using var file = new MemoryStream();
        file.Write("AOTPROFILE"u8);
        WriteInt(file, (1 << 16) | 0);
        _records.WriteTo(file);
        file.WriteByte(RecordNone);
        WriteInt(file, 0);
        return file.ToArray();
    }

    // The format is little-endian.
    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private void WriteHeader(byte recordType, int id)
    {
        _records.WriteByte(recordType);
        WriteInt(_records, id);
    }

    private void WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt(_records, bytes.Length);
        _records.Write(bytes);
    }
}
