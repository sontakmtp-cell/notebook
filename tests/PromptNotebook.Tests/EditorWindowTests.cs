using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using PromptNotebook;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.Views;

internal static class EditorWindowTests
{
    public static Task RunAsync()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var temp = Directory.CreateTempSubdirectory("notebook-window-test-");
                var app = new App(new SettingsService(Path.Combine(temp.FullName, "settings.json")));
                Exception? failure = null;
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await RunOnDispatcherAsync((MainWindow)app.MainWindow, temp.FullName);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { app.Shutdown(); }
                });
                app.Run();
                temp.Delete(recursive: true);
                if (failure is not null)
                    result.SetException(failure);
                else
                    result.SetResult();
            }
            catch (Exception ex) { result.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return result.Task;
    }

    private static async Task RunOnDispatcherAsync(MainWindow window, string folder)
    {
        var markdown = Path.Combine(folder, "mẫu.md");
        var json = Path.Combine(folder, "mẫu.json");
        var image = Path.Combine(folder, "image.png");
        const string source = "# **Tiếng Việt**\r\n\r\nĐang sửa\r\n\r\n```json\r\n{\"ok\":true}\r\n```\r\n\r\n![local](image.png)\r\n![remote](https://example.org/never-fetch.png)\r\n\r\n---\r\n";
        await TextFiles.WriteAsync(markdown, source, TextEncoding.Utf8Bom);
        await TextFiles.WriteAsync(json, """{"chữ":true}""", TextEncoding.Utf8);
        await File.WriteAllBytesAsync(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jP1sAAAAASUVORK5CYII="));
        await window.OpenFromStartupAsync(new[] { markdown, json, markdown });
        var editor = (TextEditor)window.FindName("Editor");
        var tabs = (ListBox)window.FindName("Tabs");
        var view = editor.TextArea.TextView;
        Assert(tabs.Items.Count == 2 && editor.Text == source, "Open and switch two real editor tabs");
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        await ObserveAsync(window, () =>
            view.GetVisualLine(1)?.Elements.Any(e => e.DocumentLength > e.VisualLength) == true &&
            view.GetVisualLine(5)?.Elements.OfType<InlineObjectElement>().Any(e => e.Element is Button) == true &&
            view.GetVisualLine(9)?.Elements.OfType<InlineObjectElement>().Any(e => e.Element is Image) == true,
            "Projected heading, code button and decoded local image");
        var heading = view.GetVisualLine(1)!;
        var styled = heading.Elements.First(e => e is VisualLineText);
        Assert(styled.TextRunProperties.FontRenderingEmSize > editor.FontSize &&
            styled.TextRunProperties.Typeface.Weight == FontWeights.Bold, "Real WPF font styles");
        Assert(view.GetVisualLine(10)?.Elements.OfType<InlineObjectElement>().Any() == false,
            "Remote image remains a text placeholder");
        Assert(view.GetVisualLine(6)?.Elements.Any(e => e.TextRunProperties.ForegroundBrush is SolidColorBrush brush &&
            brush.Color != ((SolidColorBrush)editor.Foreground).Color) == true, "Fenced JSON token colors");

        editor.CaretOffset = 4;
        await ObserveAsync(window, () => view.GetVisualLine(1)?.Elements.All(e => e is VisualLineText) == true,
            "Caret reveals heading syntax in the same editor");
        editor.Select(4, 10);
        Assert(editor.SelectedText == "Tiếng Việt", "Selection points to source text");
        editor.SelectedText = "Chào Khầy";
        Assert(editor.Text.StartsWith("# **Chào Khầy**", StringComparison.Ordinal), "Editing formatted text retains markers");
        editor.Undo();
        Assert(editor.Text == source, "Undo restores exact Markdown");
        editor.Redo();
        Assert(editor.Text.StartsWith("# **Chào Khầy**", StringComparison.Ordinal), "Redo restores exact edit");
        editor.Undo();
        var active = (EditorTab)tabs.SelectedItem;
        await TextFiles.WriteAsync(markdown, active.Document.Text, active.Encoding);
        Assert(await TextFiles.ReadAsync(markdown) == new LoadedText(source, TextEncoding.Utf8Bom), "Reopen saved Markdown");

        tabs.SelectedIndex = 1;
        Assert(editor.SyntaxHighlighting is not null && editor.Text.Contains("chữ"), "JSON editor active");
        var theme = (MenuItem)window.FindName("DarkModeItem");
        theme.IsChecked = false;
        theme.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert(((SolidColorBrush)editor.Background).Color == Colors.White, "Light mode on actual editor");
        tabs.SelectedIndex = 0;
        await ObserveAsync(window, () => view.GetVisualLine(5)?.Elements.OfType<InlineObjectElement>().Any() == true,
            "Returning to Markdown reinstates live rendering in light mode");
        theme.IsChecked = true;
        theme.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert(((SolidColorBrush)editor.Background).Color != Colors.White, "Dark mode on actual editor");
        window.Close();
        await ObserveAsync(window, () => !window.IsVisible, "Clean window closes");
        Console.WriteLine("[PASS] Real WPF window, tabs, live Markdown, local images, themes, source editing and selection");
    }

    private static async Task ObserveAsync(Window window, Func<bool> condition, string label)
    {
        var until = DateTime.UtcNow.AddSeconds(4);
        do
        {
            window.UpdateLayout();
            if (condition())
                return;
            await Task.Delay(40);
        } while (DateTime.UtcNow < until);
        Assert(false, label);
    }

    private static void Assert(bool test, string label)
    {
        if (!test)
            throw new InvalidOperationException(label);
    }
}
