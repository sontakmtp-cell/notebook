using System.Diagnostics;
using System.IO;
using System.Text;
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
    public static Task RunAsync(bool checkClipboard)
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
                        await RunOnDispatcherAsync((MainWindow)app.MainWindow, temp.FullName, checkClipboard);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        if (app.MainWindow?.FindName("Tabs") is ListBox tabs)
                        {
                            foreach (var tab in tabs.Items.OfType<EditorTab>())
                                tab.Document.UndoStack.MarkAsOriginalFile();
                        }
                    }
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

    private static async Task RunOnDispatcherAsync(MainWindow window, string folder, bool checkClipboard)
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
        if (checkClipboard)
        {
            var codeCopy = view.GetVisualLine(5)!.Elements.OfType<InlineObjectElement>().Select(e => e.Element).OfType<Button>().Single();
            ExerciseClipboard(editor, codeCopy, source);
        }

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
        await window.SaveActiveAsync(false);
        Assert(await TextFiles.ReadAsync(markdown) == new LoadedText(source, TextEncoding.Utf8Bom), "Reopen saved Markdown");

        tabs.SelectedIndex = 1;
        Assert(editor.SyntaxHighlighting is not null && editor.Text.Contains("chữ"), "JSON editor active");
        var originalWidth = window.Width;
        window.Width = window.MinWidth;
        window.UpdateLayout();
        var fileTools = (FrameworkElement)window.FindName("FileTools");
        var jsonTools = (FrameworkElement)window.FindName("JsonTools");
        var fileToolsRight = fileTools.TransformToAncestor(window).Transform(new Point(fileTools.ActualWidth, 0)).X;
        var jsonToolsLeft = jsonTools.TransformToAncestor(window).Transform(new Point(0, 0)).X;
        Assert(fileToolsRight <= jsonToolsLeft, "File and JSON tools fit at minimum window width");
        window.Width = originalWidth;
        var compactJson = editor.Text;
        await window.RunJsonActionAsync(true);
        Assert(editor.Document.LineCount == 3 && !editor.Text.Contains("\r\r\n", StringComparison.Ordinal),
            "JSON formatting does not insert blank lines on Windows");
        editor.Undo();
        Assert(editor.Text == compactJson, "Undo restores JSON before formatting");
        editor.Redo();
        await window.RunJsonActionAsync(false);
        Assert(editor.Text == compactJson, "Minify restores original JSON values on one line");
        editor.Undo();
        editor.Undo();
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

        var findBox = (TextBox)window.FindName("FindQuery");
        var findNext = (Button)window.FindName("FindNextButton");
        findBox.Text = "tiẾng viỆt";
        findNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(editor.SelectedText == "Tiếng Việt", "Find wraps and matches Vietnamese case-insensitively");

        const string markdownEdit = "# Đã sửa\r\n" + source;
        editor.Document.Replace(0, editor.Document.TextLength, markdownEdit);
        var status = (TextBlock)window.FindName("SaveStatus");
        await ObserveAsync(window, () => status.Text.Contains("đã giữ bản nháp", StringComparison.Ordinal), "Debounced draft status");
        Assert((await TextFiles.ReadAsync(markdown)).Text == source, "The editor leaves the original untouched before Save");

        tabs.SelectedIndex = 1;
        editor.Document.Replace(0, editor.Document.TextLength, "{\n  ");
        editor.CaretOffset = editor.Document.TextLength;
        editor.TextArea.PerformTextInput("}");
        Assert(editor.Text == "{\n}", "Closing bracket auto-dedents in the actual JSON editor");
        editor.Undo();
        Assert(editor.Text == "{\n  ", "One undo reverses bracket insertion and auto-indent");
        editor.Redo();
        Assert(editor.Text == "{\n}", "One redo reapplies bracket insertion and auto-indent");
        await window.SaveAllAsync();
        Assert((await TextFiles.ReadAsync(markdown)).Text == markdownEdit &&
            (await TextFiles.ReadAsync(json)).Text == "{\n}", "Save All persists both formats");
        var settings = new SettingsService(Path.Combine(folder, "settings.json")).Load();
        Assert(settings.RecentFiles.Count == 2 && settings.RecentFiles[0] == json, "Recent files persist in JSON settings");

        await ExerciseLargeMarkdownAsync(window, folder, editor, view);
        window.Close();
        await ObserveAsync(window, () => !window.IsVisible, "Clean window closes");
        Console.WriteLine("[PASS] Real WPF window, tabs, live Markdown, local images, themes, source editing and selection");
    }

    private static void ExerciseClipboard(TextEditor editor, Button codeCopy, string source)
    {
        var previous = Clipboard.GetDataObject();
        try
        {
            codeCopy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(Clipboard.GetText() == """{"ok":true}""", "Native button copies fenced source without markers");

            editor.Select(0, editor.Document.GetLineByNumber(1).Length);
            editor.Copy();
            Assert(Clipboard.GetText() == "# **Tiếng Việt**", "Copy carries the original Markdown source");
            editor.Select(editor.Document.TextLength, 0);
            editor.Paste();
            Assert(editor.Text.EndsWith("# **Tiếng Việt**", StringComparison.Ordinal), "Paste goes to the same Markdown document");
            editor.Undo();
            Assert(editor.Text == source, "Paste is one undo action");
            Console.WriteLine("[PASS] Native WPF clipboard, Markdown copy / paste and fenced code button");
        }
        finally
        {
            if (previous is null)
                Clipboard.Clear();
            else
                Clipboard.SetDataObject(previous, true);
        }
    }

    private static async Task ExerciseLargeMarkdownAsync(MainWindow window, string folder, TextEditor editor, TextView view)
    {
        var path = Path.Combine(folder, "large.md");
        const string section = "# Tiêu đề tệp lớn\n\nMột đoạn dài có **định dạng** và Unicode 中文 😀.\n\n";
        const int sections = 16_000;
        var text = new StringBuilder(sections * section.Length);
        for (var index = 0; index < sections; index++)
            text.Append(section);
        await File.WriteAllTextAsync(path, text.ToString());
        var duration = Stopwatch.StartNew();
        await window.OpenFromStartupAsync([path]);
        var open = duration.ElapsedMilliseconds;
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        await ObserveAsync(window,
            () => view.GetVisualLine(1)?.Elements.Any(element => element.DocumentLength > element.VisualLength) == true,
            "Large Markdown renders the first viewport");
        Assert(editor.Document.LineCount == sections * 4 + 1 && view.VisualLines.Count < 250,
            "Large Markdown keeps an editor-sized visible region");
        Console.WriteLine($"[PASS] Large Markdown: {new FileInfo(path).Length:N0} bytes, {editor.Document.LineCount:N0} lines, " +
            $"open {open} ms, formatted viewport {duration.ElapsedMilliseconds} ms, {view.VisualLines.Count} visible lines");
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
