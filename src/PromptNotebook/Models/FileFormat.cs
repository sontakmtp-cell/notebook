using System.IO;

namespace PromptNotebook.Models;

public enum FileFormat { Txt, Json, Markdown }

public enum TextEncoding { Utf8, Utf8Bom, Utf16Le, Utf16Be, Utf16LeNoBom, Utf16BeNoBom }

public static class FileFormats
{
    public static FileFormat FromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".txt" => FileFormat.Txt,
        ".json" => FileFormat.Json,
        ".md" => FileFormat.Markdown,
        _ => throw new NotSupportedException("Notebook hỗ trợ tệp .txt, .json và .md.")
    };

    public static string Extension(this FileFormat format) => format switch
    {
        FileFormat.Json => ".json",
        FileFormat.Markdown => ".md",
        _ => ".txt"
    };

    public static string Label(this FileFormat format) => format switch
    {
        FileFormat.Json => "JSON",
        FileFormat.Markdown => "Markdown",
        _ => "TXT"
    };
}
