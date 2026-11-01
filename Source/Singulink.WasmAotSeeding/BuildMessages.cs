using System.IO;
using System.Text;

namespace Singulink.WasmAotSeeding;

/// <summary>
/// Writes the warnings and errors to the files that the targets log them from: <c>&lt;folder&gt;/&lt;code&gt;.txt</c>, one message per line.
/// The targets run the generator with Exec's parsing of "warning CODE: text" lines turned off, so that no other output line can pass for one.
/// </summary>
internal sealed class BuildMessages(string folder)
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public void Warning(string code, string text) => Write(code, text);

    public void Error(string code, string text) => Write(code, text);

    // One message per line, which ReadLinesFromFile reads.
    public static string OneLine(string text) => text.ReplaceLineEndings(" ");

    private void Write(string code, string text)
    {
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, code + ".txt"), OneLine(text) + "\n", Utf8);
    }
}
