using System.Windows;
using PromptNotebook.Services;
using PromptNotebook.Views;

namespace PromptNotebook;

public partial class App : Application
{
    private readonly SettingsService _store;

    public App() : this(new SettingsService()) { }

    internal App(SettingsService store) => _store = store;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = _store.Load();
        ApplyTheme(settings.IsDarkMode);
        MainWindow = new MainWindow(_store, settings);
        MainWindow.Show();
        if (e.Args.Length > 0)
            _ = ((MainWindow)MainWindow).OpenFromStartupAsync(e.Args);
    }

    public static void ApplyTheme(bool dark)
    {
        var theme = dark ? "DarkTheme" : "LightTheme";
        var uri = new Uri($"/PromptNotebook;component/Themes/{theme}.xaml", UriKind.Relative);
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
    }
}
