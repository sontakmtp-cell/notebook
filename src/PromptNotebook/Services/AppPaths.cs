using System.IO;

namespace PromptNotebook.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookEditor");

    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
}
