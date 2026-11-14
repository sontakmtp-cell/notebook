using System.IO;
using System.Windows;
using System.Windows.Threading;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;
using PromptNotebook.Views;

namespace PromptNotebook;

public partial class App : Application
{
    private static string ErrorLogPath => Path.Combine(AppPaths.AppDataDirectory, "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogError($"AppDomain.UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();

            ApplyTheme(settings.IsDarkMode);

            // Đóng hộp đăng nhập chưa được tắt app trước khi cửa sổ chính xuất hiện.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var passwordService = new PasswordService(Path.Combine(AppContext.BaseDirectory, ".env"));
            if (new LoginWindow(passwordService).ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            var dbService = new DatabaseService();
            dbService.InitializeDatabase();

            var jsonService = new JsonExportImportService();
            var viewModel = new MainViewModel(dbService, settingsService, jsonService, settings);

            var mainWindow = new MainWindow(viewModel, settingsService, settings);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            viewModel.Initialize(settings.LastSelectedPromptId);
        }
        catch (Exception ex)
        {
            LogError($"OnStartup: {ex}");
            MessageBox.Show(
                $"Không thể khởi động ứng dụng:\n{ex.Message}",
                "Prompt Notebook",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void ApplyTheme(bool isDarkMode)
    {
        var app = Current;
        if (app is null) return;

        var themeUri = new Uri(
            isDarkMode ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml",
            UriKind.Relative);

        var newDict = new ResourceDictionary { Source = themeUri };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0)
        {
            merged[0] = newDict;
        }
        else
        {
            merged.Add(newDict);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError($"DispatcherUnhandledException: {e.Exception}");

        e.Handled = true;
        MessageBox.Show(
            $"Đã xảy ra lỗi ngoài dự kiến:\n{e.Exception.Message}",
            "Prompt Notebook",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void LogError(string message)
    {
        try
        {
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:O}] {message}\n");
        }
        catch
        {
            // Bỏ qua nếu không thể ghi file log
        }
    }
}
