using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

/// <summary>
/// Hỗ trợ Nhập / Xuất toàn bộ dữ liệu Prompt theo chuẩn JSON UTF-8 (không mã hóa ký tự tiếng Việt / tiếng Trung thành \uXXXX).
/// </summary>
public sealed class JsonExportImportService
{
    public sealed class PromptExportPackage
    {
        [JsonPropertyName("application")]
        public string Application { get; set; } = "Prompt Notebook";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("exportedAt")]
        public string ExportedAt { get; set; } = DateTime.UtcNow.ToString("O");

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("prompts")]
        public List<PromptItem> Prompts { get; set; } = [];
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Xuất danh sách prompt ra file JSON chuẩn UTF-8.
    /// </summary>
    public int ExportToJsonFile(DatabaseService db, string filePath, bool includeDeleted = false)
    {
        var prompts = db.GetAllPromptsForExport(includeDeleted);
        var package = new PromptExportPackage
        {
            ExportedAt = DateTime.UtcNow.ToString("O"),
            Count = prompts.Count,
            Prompts = prompts
        };

        var json = JsonSerializer.Serialize(package, SerializerOptions);
        File.WriteAllText(filePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return prompts.Count;
    }

    /// <summary>
    /// Nhập dữ liệu prompt từ file JSON UTF-8 (hỗ trợ cả cấu trúc gói đối tượng hoặc mảng JSON thuần).
    /// </summary>
    public int ImportFromJsonFile(DatabaseService db, string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Không tìm thấy file JSON cần nhập.", filePath);

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        List<PromptItem>? items = null;
        var trimmed = json.TrimStart();

        if (trimmed.StartsWith('['))
        {
            items = JsonSerializer.Deserialize<List<PromptItem>>(json, SerializerOptions);
        }
        else
        {
            var package = JsonSerializer.Deserialize<PromptExportPackage>(json, SerializerOptions);
            items = package?.Prompts;
        }

        if (items is null || items.Count == 0)
            return 0;

        return db.BulkInsertPrompts(items);
    }
}
