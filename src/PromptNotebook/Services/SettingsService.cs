using System.IO;
using System.Text.Json;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

public sealed class SettingsService(string? settingsFilePath = null)
{
    private readonly string _path = settingsFilePath ?? AppPaths.SettingsPath;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) is { } settings)
            {
                settings.EditorFontSize = ValidSize(settings.EditorFontSize, 10, 36, 16);
                settings.WindowWidth = ValidSize(settings.WindowWidth, 620, 8000, 1080);
                settings.WindowHeight = ValidSize(settings.WindowHeight, 400, 8000, 720);
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Settings are optional. A damaged settings file should not prevent startup.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the editor usable when the settings directory is unavailable.
        }
    }

    private static double ValidSize(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) && value >= minimum && value <= maximum ? value : fallback;
}
