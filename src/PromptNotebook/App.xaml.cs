using System.IO;
using System.Windows;
using PromptNotebook.Services;
using PromptNotebook.Views;

namespace PromptNotebook;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new SettingsService();
        var settings = store.Load();
        ApplyTheme(settings.IsDarkMode);
        MainWindow = new MainWindow(store, settings);
        MainWindow.Show();
    }

    public static void ApplyTheme(bool dark)
    {
        var uri = new Uri(dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
    }
}
