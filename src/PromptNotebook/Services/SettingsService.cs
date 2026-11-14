using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

/// <summary>
/// Đọc và ghi cấu hình ứng dụng (Dark/Light Mode, độ rộng cột, vị trí cửa sổ, cỡ chữ).
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SettingsService(string? customSettingsFilePath = null)
    {
        _settingsFilePath = customSettingsFilePath ?? AppPaths.SettingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return Validate(settings ?? new AppSettings());
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Validate(settings), JsonOptions);
            File.WriteAllText(_settingsFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Bỏ qua lỗi ghi cấu hình phụ nếu file bị khóa tạm thời
        }
    }

    private static AppSettings Validate(AppSettings s)
    {
        if (s.EditorFontSize < 10 || s.EditorFontSize > 36)
            s.EditorFontSize = 14.0;

        if (s.EditorViewMode is not ("Edit" or "Split" or "Preview"))
            s.EditorViewMode = "Split";

        if (s.LeftColumnWidthRatio < 15 || s.LeftColumnWidthRatio > 75)
            s.LeftColumnWidthRatio = 40.0;

        if (s.LeftColumnPixelWidth < 220 || s.LeftColumnPixelWidth > 1200)
            s.LeftColumnPixelWidth = 440.0;

        if (s.WindowWidth < 720)
            s.WindowWidth = 1180.0;

        if (s.WindowHeight < 480)
            s.WindowHeight = 740.0;

        return s;
    }
}
