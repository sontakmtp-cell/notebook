using System.Text.Json.Serialization;

namespace PromptNotebook.Models;

/// <summary>
/// Lưu trữ cấu hình giao diện và vị trí cửa sổ giữa các lần mở ứng dụng.
/// Lưu tại %LocalAppData%\PromptNotebook\settings.json
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("isDarkMode")]
    public bool IsDarkMode { get; set; } = true; // Mặc định Dark Mode

    [JsonPropertyName("leftColumnWidthRatio")]
    public double LeftColumnWidthRatio { get; set; } = 40.0; // Mặc định 40%

    [JsonPropertyName("leftColumnPixelWidth")]
    public double LeftColumnPixelWidth { get; set; } = 440.0;

    [JsonPropertyName("editorFontSize")]
    public double EditorFontSize { get; set; } = 14.0;

    [JsonPropertyName("editorViewMode")]
    public string EditorViewMode { get; set; } = "Split"; // Edit | Split | Preview

    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1180.0;

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 740.0;

    [JsonPropertyName("windowLeft")]
    public double? WindowLeft { get; set; }

    [JsonPropertyName("windowTop")]
    public double? WindowTop { get; set; }

    [JsonPropertyName("isMaximized")]
    public bool IsMaximized { get; set; }

    [JsonPropertyName("lastSelectedPromptId")]
    public long? LastSelectedPromptId { get; set; }
}
