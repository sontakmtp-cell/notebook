using System.Windows;
using System.Windows.Threading;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;
using PromptNotebook.Views;

namespace PromptNotebook;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = System.IO.Path.Combine(AppPaths.AppDataDirectory, "startup.log");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(logPath, $"AppDomain.UnhandledException: {args.ExceptionObject}\n");
            }
            catch { }
        };

        try
        {
            System.IO.File.WriteAllText(logPath, $"Starting at {DateTime.Now:O}\n");

            // Bắt các lỗi ngoại lệ không mong muốn để không sập ứng dụng đột ngột
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var settingsService = new SettingsService();
            var settings = settingsService.Load();

            ApplyTheme(settings.IsDarkMode);

            var dbService = new DatabaseService();
            dbService.InitializeDatabase();

            var jsonService = new JsonExportImportService();
            var viewModel = new MainViewModel(dbService, settingsService, jsonService, settings);

            var mainWindow = new MainWindow(viewModel, settingsService, settings);
            mainWindow.Closing += (_, _) =>
            {
                try { System.IO.File.AppendAllText(logPath, $"MainWindow.Closing at {DateTime.Now:O}\n"); } catch { }
            };
            mainWindow.Closed += (_, _) =>
            {
                try { System.IO.File.AppendAllText(logPath, $"MainWindow.Closed at {DateTime.Now:O}\n"); } catch { }
            };

            MainWindow = mainWindow;
            mainWindow.Show();

            var handle = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
            System.IO.File.AppendAllText(logPath, $"MainWindow Handle: 0x{handle:X}, Title: '{mainWindow.Title}', Visibility: {mainWindow.Visibility}\n");

            viewModel.Initialize(settings.LastSelectedPromptId);
            System.IO.File.AppendAllText(logPath, $"Startup finished successfully at {DateTime.Now:O}\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(logPath, $"ERROR in OnStartup:\n{ex}\n");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        var logPath = System.IO.Path.Combine(AppPaths.AppDataDirectory, "startup.log");
        try
        {
            System.IO.File.AppendAllText(logPath, $"App.OnExit with code {e.ApplicationExitCode} at {DateTime.Now:O}\n");
        }
        catch { }
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
        var logPath = System.IO.Path.Combine(AppPaths.AppDataDirectory, "startup.log");
        try
        {
            System.IO.File.AppendAllText(logPath, $"DispatcherUnhandledException: {e.Exception}\n");
        }
        catch { }

        e.Handled = true;
        MessageBox.Show(
            $"Đã xảy ra lỗi ngoài dự kiến:\n{e.Exception.Message}",
            "Prompt Notebook",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
