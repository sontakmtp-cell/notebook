using System.IO;
using System.Text;
using System.Text.Json;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using PromptNotebook.Helpers;
using PromptNotebook.Models;
using PromptNotebook.Services;

var first = new EditorTab("One");
var second = new EditorTab("Two");
first.Document.Insert(0, "Tiếng Việt · 中文 · 😀");
Assert(first.IsDirty && !second.IsDirty && second.Document.TextLength == 0, "Independent documents");
Assert(first.CharacterCount == first.Document.TextLength - 1, "An emoji counts as one character");
first.Document.UndoStack.Undo();
Assert(!first.IsDirty && first.Document.TextLength == 0 && first.CharacterCount == 0, "Undo returns to saved state");
first.Document.UndoStack.Redo();
Assert(first.IsDirty && first.Document.Text.Contains("Tiếng Việt"), "Redo restores Unicode text");
first.Document.UndoStack.MarkAsOriginalFile();
Assert(!first.IsDirty, "Saved state clears dirty indicator");

Console.WriteLine("[PASS] Tab documents, Unicode, undo, redo and saved state");

var directory = Directory.CreateTempSubdirectory("notebook-tests-");
try
{
    var sample = "Tiếng Việt · 中文 · 😀\r\nSecond line\nThird line\r\n";
    var codecs = new (Encoding Codec, TextEncoding Expected)[]
    {
        (new UTF8Encoding(false), TextEncoding.Utf8),
        (new UTF8Encoding(true), TextEncoding.Utf8Bom),
        (new UnicodeEncoding(false, true), TextEncoding.Utf16Le),
        (new UnicodeEncoding(true, true), TextEncoding.Utf16Be),
        (new UnicodeEncoding(false, false), TextEncoding.Utf16LeNoBom),
        (new UnicodeEncoding(true, false), TextEncoding.Utf16BeNoBom)
    };
    foreach (var (codec, expected) in codecs)
    {
        var path = Path.Combine(directory.FullName, $"{expected}.txt");
        var bytes = codec.GetPreamble().Concat(codec.GetBytes(sample)).ToArray();
        File.WriteAllBytes(path, bytes);
        var opened = await TextFiles.ReadAsync(path);
        Assert(opened.Encoding == expected && opened.Text == sample, $"Read {expected}");
        await TextFiles.WriteAsync(path, opened.Text, opened.Encoding);
        Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), $"Byte-for-byte round trip {expected}");
        await TextFiles.WriteAsync(path, opened.Text + "Đã sửa", opened.Encoding);
        Assert(await TextFiles.ReadAsync(path) == opened with { Text = sample + "Đã sửa" }, $"Save edits {expected}");
    }
    Assert(directory.GetFiles().Length == codecs.Length, "No temporary files after save");
    Console.WriteLine("[PASS] TXT editing, six encodings, Unicode, LF / CRLF and atomic replacements");
}
finally
{
    directory.Delete(recursive: true);
}

const string json = """{"chữ":"Tiếng Việt","số":1.20e-10,"items":[true,{"ok":null}]}""";
var formatted = JsonEditing.Format(json, true, "\r\n");
Assert(formatted.StartsWith("{\r\n  \"chữ\"", StringComparison.Ordinal) &&
    !formatted.Contains("\r\r\n", StringComparison.Ordinal) &&
    !JsonEditing.Format(json, true, "\n").Contains('\r') &&
    JsonEditing.Format(formatted, false, "\n") == json,
    "JSON format / minify retains values and original numeric lexemes");
try
{
    JsonEditing.Format("{\n \"chữ\": }", true, "\n");
    throw new InvalidOperationException("Malformed JSON accepted");
}
catch (JsonException ex)
{
    Assert(ex.LineNumber == 1 && ex.BytePositionInLine > 5, "Malformed JSON reports the UTF-8 position");
}
var indent = new TextDocument("{\n\"items\": [\n0\n]\n}");
JsonIndentation.Instance.IndentLines(indent, 2, 5);
Assert(indent.Text == "{\n  \"items\": [\n    0\n  ]\n}", "Nested JSON indentation");
foreach (var dark in new[] { true, false })
{
    var document = new TextDocument(json);
    using var highlighter = new DocumentHighlighter(document, JsonHighlighting.ForTheme(dark));
    Assert(highlighter.HighlightLine(1).Sections.Count >= 7, "JSON token highlighting in both themes");
}
Console.WriteLine("[PASS] JSON format / minify, validation, indentation and both color palettes");
MarkdownTests.Run();
await RecoveryTests.RunAsync();
await EditorWindowTests.RunAsync(args.Contains("--clipboard", StringComparer.Ordinal),
    Environment.GetEnvironmentVariable("NOTEBOOK_TEST_TABLE_SAMPLE"));

static void Assert(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}
