using System.IO;

namespace PromptNotebook.Services;

/// <summary>
/// Quản lý đường dẫn lưu trữ dữ liệu cố định tại %LocalAppData%\PromptNotebook.
/// Tuyệt đối không lưu dữ liệu quan trọng vào thư mục tạm (%TEMP%).
/// </summary>
public static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "PromptNotebook");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(AppDataDirectory, "prompts.db");

    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static string BackupsDirectory
    {
        get
        {
            var dir = Path.Combine(AppDataDirectory, "Backups");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }
    }
}
