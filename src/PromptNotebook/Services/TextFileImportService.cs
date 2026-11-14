using System.IO;
using System.Text;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

public sealed record TextFileImportError(string FilePath, string Message);
public sealed record TextFileImportResult(int ImportedCount, IReadOnlyList<TextFileImportError> Errors);

/// <summary>Đọc TXT/Markdown thành prompt mới, giữ nguyên nội dung và dấu xuống dòng.</summary>
public sealed class TextFileImportService
{
    public static bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<(List<PromptItem> Prompts, List<TextFileImportError> Errors)> ReadFilesAsync(
        IEnumerable<string> filePaths, string category = "Chung")
    {
        var prompts = new List<PromptItem>();
        var errors = new List<TextFileImportError>();

        foreach (var filePath in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsSupportedFile(filePath))
            {
                errors.Add(new(filePath, "Chỉ hỗ trợ file .txt và .md."));
                continue;
            }

            try
            {
                // UTF-8 mặc định; tự nhận BOM của UTF-8/UTF-16/UTF-32.
                // Báo lỗi thay vì âm thầm thay ký tự khi file không phải Unicode hợp lệ.
                using var reader = new StreamReader(filePath, new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: true);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                prompts.Add(new PromptItem
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Content = content,
                    Category = category
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       DecoderFallbackException or ArgumentException or NotSupportedException)
            {
                errors.Add(new(filePath, ex is DecoderFallbackException
                    ? "Không đọc được bảng mã. Hãy lưu file dưới dạng UTF-8 rồi nhập lại."
                    : ex.Message));
            }
        }

        return (prompts, errors);
    }
}
