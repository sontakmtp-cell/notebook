using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PromptNotebook.Services;

public static class JsonEditing
{
    public static string Format(string source, bool indented, string newLine)
    {
        using var document = JsonDocument.Parse(source);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            document.RootElement.WriteTo(writer);
        }

        var result = Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return newLine == "\n" ? result : result.Replace("\n", newLine, StringComparison.Ordinal);
    }
}
