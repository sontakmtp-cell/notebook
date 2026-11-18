using System.IO;
using System.Text;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

public sealed record LoadedText(string Text, TextEncoding Encoding);
internal readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
{
    public static FileStamp? Read(string path)
    {
        var file = new FileInfo(path);
        try { return new FileStamp(file.Length, file.LastWriteTimeUtc); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }
}

public static class TextFiles
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf8Bom = new UTF8Encoding(true, true);
    private static readonly Encoding Utf16Le = new UnicodeEncoding(false, true, true);
    private static readonly Encoding Utf16Be = new UnicodeEncoding(true, true, true);
    private static readonly Encoding Utf16LeNoBom = new UnicodeEncoding(false, false, true);
    private static readonly Encoding Utf16BeNoBom = new UnicodeEncoding(true, false, true);

    public static async Task<LoadedText> ReadAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        return Decode(bytes);
    }

    public static LoadedText Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) ||
            bytes.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
            throw new NotSupportedException("Tệp UTF-32 cần được chuyển sang UTF-8 hoặc UTF-16.");

        var encoding = bytes.StartsWith(Utf8Bom.Preamble) ? TextEncoding.Utf8Bom :
            bytes.StartsWith(Utf16Le.Preamble) ? TextEncoding.Utf16Le :
            bytes.StartsWith(Utf16Be.Preamble) ? TextEncoding.Utf16Be : DetectWithoutBom(bytes);

        var codec = Codec(encoding);
        return new LoadedText(codec.GetString(bytes[codec.Preamble.Length..]), encoding);
    }

    private static TextEncoding DetectWithoutBom(ReadOnlySpan<byte> bytes)
    {
        var length = Math.Min(bytes.Length, 4096);
        if ((bytes.Length & 1) == 0 && length >= 2)
        {
            int evenZeroes = 0, oddZeroes = 0;
            for (var i = 0; i < length; i += 2)
            {
                if (bytes[i] == 0)
                    evenZeroes++;
                if (bytes[i + 1] == 0)
                    oddZeroes++;
            }

            // ASCII, line endings and common punctuation provide a reliable byte-order signal.
            if (oddZeroes > evenZeroes * 4 && oddZeroes * 12 >= length)
                return TextEncoding.Utf16LeNoBom;
            if (evenZeroes > oddZeroes * 4 && evenZeroes * 12 >= length)
                return TextEncoding.Utf16BeNoBom;
        }

        return TextEncoding.Utf8;
    }

    public static Task WriteAsync(string path, string text, TextEncoding encoding) =>
        WriteCoreAsync(path, text, encoding, null, false);

    internal static Task<FileStamp> WriteCheckedAsync(string path, string text, TextEncoding encoding, FileStamp? expected) =>
        WriteCoreAsync(path, text, encoding, expected, true);

    private static async Task<FileStamp> WriteCoreAsync(string path, string text, TextEncoding encoding,
        FileStamp? expected, bool checkVersion)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".notebook-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, text, Codec(encoding)).ConfigureAwait(false);
            var current = FileStamp.Read(path);
            if (checkVersion && current != expected)
                throw new IOException("Tệp đích vừa thay đổi. Hãy kiểm tra nội dung trên đĩa và thử lại.");

            if (current is not null)
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
            return FileStamp.Read(path) ?? throw new IOException("Tệp đã lưu không còn tồn tại tại đường dẫn đích.");
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string Label(this TextEncoding encoding) => encoding switch
    {
        TextEncoding.Utf8Bom => "UTF-8 BOM",
        TextEncoding.Utf16Le => "UTF-16 LE",
        TextEncoding.Utf16Be => "UTF-16 BE",
        TextEncoding.Utf16LeNoBom => "UTF-16 LE (không BOM)",
        TextEncoding.Utf16BeNoBom => "UTF-16 BE (không BOM)",
        _ => "UTF-8"
    };

    private static Encoding Codec(TextEncoding encoding) => encoding switch
    {
        TextEncoding.Utf8Bom => Utf8Bom,
        TextEncoding.Utf16Le => Utf16Le,
        TextEncoding.Utf16Be => Utf16Be,
        TextEncoding.Utf16LeNoBom => Utf16LeNoBom,
        TextEncoding.Utf16BeNoBom => Utf16BeNoBom,
        _ => Utf8
    };
}
