using System.IO;
using System.Text.Json;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

public sealed class SettingsService(string? settingsFilePath = null)
{
    private readonly string _path = settingsFilePath ?? AppPaths.SettingsPath;
    internal string RecoveryRoot => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, "Recovery");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) is { } settings)
            {
                settings.EditorFontSize = ValidSize(settings.EditorFontSize, 10, 36, 16);
                settings.WindowWidth = ValidSize(settings.WindowWidth, 620, 8000, 1080);
                settings.WindowHeight = ValidSize(settings.WindowHeight, 400, 8000, 720);
                settings.RecentFiles = (settings.RecentFiles ?? []).Where(path => !string.IsNullOrWhiteSpace(path))
                    .Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
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
        var fullPath = Path.GetFullPath(_path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, fullPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the editor usable when the settings directory is unavailable.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static double ValidSize(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) && value >= minimum && value <= maximum ? value : fallback;
}
