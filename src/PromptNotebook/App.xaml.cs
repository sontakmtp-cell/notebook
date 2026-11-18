using System.IO;
using System.Windows;
using System.Windows.Threading;
using PromptNotebook.Services;
using PromptNotebook.Views;

namespace PromptNotebook;

public partial class App : Application
{
    private readonly SettingsService _store;
    private readonly bool _singleInstanceEnabled;
    private SingleInstance? _instance;

    public App() : this(new SettingsService()) => _singleInstanceEnabled = true;

    internal App(SettingsService store) => _store = store;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = Array.ConvertAll(e.Args, ResolveLaunchPath);
        if (_singleInstanceEnabled)
        {
            _instance = new SingleInstance();
            if (!await StartOrForwardAsync(_instance, paths))
                return;
        }

        var settings = _store.Load();
        ApplyTheme(settings.IsDarkMode);
        var window = new MainWindow(_store, settings);
        MainWindow = window;
        window.Show();
        _ = window.StartAsync(paths);
        if (_instance is not null)
            _ = _instance.ListenAsync((request, cancellationToken) => Dispatcher.InvokeAsync(
                () => window.QueueExternalOpen(request), DispatcherPriority.Normal, cancellationToken).Task);
    }

    private async Task<bool> StartOrForwardAsync(SingleInstance instance, string[] paths)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!deadline.IsCancellationRequested)
        {
            if (instance.TryAcquire())
                return true;
            if (await instance.TryForwardAsync(paths, deadline.Token))
            {
                Shutdown();
                return false;
            }
            await Task.Delay(80);
        }

        MessageBox.Show("Không thể chuyển yêu cầu tới cửa sổ Notebook đang mở. Hãy thử mở tệp lại.",
            "Notebook", MessageBoxButton.OK, MessageBoxImage.Warning);
        Shutdown(1);
        return false;
    }

    private static string ResolveLaunchPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return path; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    public static void ApplyTheme(bool dark)
    {
        var theme = dark ? "DarkTheme" : "LightTheme";
        var uri = new Uri($"/PromptNotebook;component/Themes/{theme}.xaml", UriKind.Relative);
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
    }
}
