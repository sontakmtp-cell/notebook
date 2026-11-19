using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using PromptNotebook;
using PromptNotebook.Markdown;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.Views;

internal static class EditorWindowTests
{
    public static Task RunAsync(bool checkClipboard, string? tableSample)
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
                        await RunOnDispatcherAsync((MainWindow)app.MainWindow, temp.FullName, checkClipboard, tableSample);
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
                try { temp.Delete(recursive: true); }
                catch (IOException) when (failure is not null) { }
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

    private static async Task RunOnDispatcherAsync(MainWindow window, string folder, bool checkClipboard, string? tableSample)
    {
        Assert(window.Icon is BitmapFrame { Decoder: IconBitmapDecoder decoder } &&
            decoder.Frames.Any(frame => frame.PixelWidth == 16 && frame.PixelHeight == 16) &&
            decoder.Frames.Any(frame => frame.PixelWidth == 256 && frame.PixelHeight == 256),
            "The main window loads the multi-resolution Notebook icon");

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

        var activeTab = (EditorTab)tabs.SelectedItem;
        var saveBinding = window.InputBindings.OfType<KeyBinding>()
            .Single(binding => binding.Key == Key.S && binding.Modifiers == ModifierKeys.Control);
        var save = (RoutedCommand)saveBinding.Command;
        Assert(save.CanExecute(null, editor.TextArea), "Ctrl+S is available from the editor");
        save.Execute(null, editor.TextArea);
        await ObserveAsync(window, () => !activeTab.IsDirty, "Ctrl+S saves the edited tab");
        Assert((await TextFiles.ReadAsync(markdown)).Text.StartsWith("# **Chào Khầy**", StringComparison.Ordinal),
            "Ctrl+S writes the edit to disk");

        editor.Undo();
        var findQuery = (TextBox)window.FindName("FindQuery");
        save.Execute(null, findQuery);
        await ObserveAsync(window, () => !activeTab.IsDirty, "Ctrl+S can save from the search toolbar");
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

        ExerciseReplace(window, editor, tabs);
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

        var extra = Path.Combine(folder, "tệp mở thêm.txt");
        var longLine = new string('x', 250);
        await File.WriteAllLinesAsync(extra, Enumerable.Range(1, 200).Select(number => $"{number:D3} {longLine}")
            .Prepend("# Tiêu đề trong TXT"));
        var existingMarkdown = tabs.Items.OfType<EditorTab>().Single(tab => tab.FilePath == markdown);
        const string unsaved = "Thay đổi chưa lưu.";
        existingMarkdown.Document.Insert(existingMarkdown.Document.TextLength, unsaved);
        window.WindowState = WindowState.Minimized;
        var opening = window.OpenFromStartupAsync([extra]);
        Assert(window.QueueExternalOpen([json, extra, markdown]), "Accept launch while a file open is in progress");
        await opening;
        await ObserveAsync(window, () => (tabs.SelectedItem as EditorTab)?.FilePath == markdown &&
            window.WindowState != WindowState.Minimized, "Queued launch restores the window and selects its last file");
        Assert(tabs.Items.Count == 3, "Queued launch reuses tabs for paths that are already open");
        Assert(existingMarkdown.IsDirty && editor.Text.EndsWith(unsaved, StringComparison.Ordinal),
            "Opening an existing tab preserves its unsaved edits");
        editor.Undo();

        SelectFileTab(tabs, extra);
        editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset;
        await ObserveAsync(window, () => view.GetVisualLine(1)?.Elements.Any(e =>
            e is VisualLineText && e.TextRunProperties.FontRenderingEmSize > editor.FontSize) == true,
            "Markdown headings are rendered in TXT");
        Assert(view.GetVisualLine(2)?.Elements.All(e => e is VisualLineText) == true &&
            editor.Text.StartsWith("# Tiêu đề trong TXT", StringComparison.Ordinal),
            "Plain TXT and source remain intact");

        await ExerciseTablesAsync(window, folder, editor, tableSample);
        await ExerciseLargeMarkdownAsync(window, folder, editor, view);
        await ExerciseSessionRestoreAsync(window, folder, markdown, json, extra, markdownEdit);
        Console.WriteLine("[PASS] Real WPF window, tabs, live Markdown, local images, themes, source editing and selection");
    }

    private static async Task ExerciseTablesAsync(MainWindow window, string folder, TextEditor editor, string? tableSample)
    {
        var path = Path.Combine(folder, "table.txt");
        await File.WriteAllTextAsync(path, MarkdownTests.MultilineTable);
        await window.OpenFromStartupAsync([path]);
        var view = editor.TextArea.TextView;
        await ObserveAsync(window, () => TableGrid(view, 5) is { ColumnDefinitions.Count: 3 } &&
            TableGrid(view, 8) is { ColumnDefinitions.Count: 3 }, "Tables render as wrapping grids in TXT");
        var first = view.GetVisualLine(5)!;
        Assert(first.LastDocumentLine.LineNumber == 7, "Continuation lines are displayed as part of the same row");
        var second = TableGrid(view, 8)!;
        var text = (TextBlock)((Border)second.Children[1]).Child;
        Assert(text.TextWrapping == TextWrapping.Wrap && text.TextAlignment == TextAlignment.Center &&
            text.Inlines.OfType<LineBreak>().Count() == 2 &&
            text.Inlines.OfType<Run>().Any(run => run.Text == "Dòng đầu" && run.FontWeight == FontWeights.Bold),
            "WPF renders the line breaks and emphasis inside a cell");

        editor.CaretOffset = editor.Document.GetLineByNumber(7).Offset + 1;
        await ObserveAsync(window, () => TableGrid(view, 5) is null &&
            view.GetVisualLine(7)?.FirstDocumentLine.LineNumber == 7, "Entering a continuation reveals the original row");
        var offset = editor.Document.GetLineByNumber(7).Offset + 2;
        editor.Document.Replace(offset, " Cuối".Length, " Đã sửa");
        Assert(editor.Text.Contains(" | Đã sửa |", StringComparison.Ordinal), "A revealed row remains editable");
        editor.Undo();
        Assert(editor.Text == MarkdownTests.MultilineTable, "Editing a projected table leaves source unchanged after undo");
        editor.CaretOffset = 0;
        await ObserveAsync(window, () => TableGrid(view, 5) is not null, "Leaving the row restores the grid");

        if (tableSample is not null)
        {
            var sample = await TextFiles.ReadAsync(tableSample);
            var copy = Path.Combine(folder, "table-sample.md");
            await TextFiles.WriteAsync(copy, sample.Text, sample.Encoding);
            await window.OpenFromStartupAsync([copy]);
            var layout = MarkdownLayout.Parse(editor.Text);
            Assert(layout.TableRows.Count > 0 && editor.Text == sample.Text, "Sample tables retain the exact source document");
            foreach (var row in layout.TableRows)
            {
                editor.ScrollToLine(row.FirstLine);
                await ObserveAsync(window, () => TableGrid(view, row.FirstLine)?.ColumnDefinitions.Count == row.Cells.Length,
                    $"Sample table row starting on line {row.FirstLine} renders all columns");
            }
            var last = layout.TableRows[^1].FirstLine;
            var originalWidth = window.Width;
            var gridWidth = TableGrid(view, last)!.Width;
            window.Width = window.MinWidth;
            await ObserveAsync(window, () => TableGrid(view, last) is { } grid && grid.Width < gridWidth,
                "The sample's wide table resizes with the editor");
            window.Width = originalWidth;
            Console.WriteLine($"[PASS] User sample rendered {layout.TableRows.Count} table rows, including {layout.TableRows.Count(row => row.IsHeader)} headers");
        }
        Console.WriteLine("[PASS] Multiline tables render aligned cells and reveal editable original rows");
    }

    private static Grid? TableGrid(TextView view, int line) => view.GetVisualLine(line)?.Elements
        .OfType<InlineObjectElement>().Select(element => element.Element).OfType<Grid>().SingleOrDefault();

    private static void ExerciseReplace(MainWindow window, TextEditor editor, ListBox tabs)
    {
        var original = editor.Text;
        const string appended = "Tiếng Việt | TIẾNG VIỆT | tiếng việt";
        editor.Document.Insert(original.Length, appended);
        var source = editor.Text;
        var otherTab = (EditorTab)tabs.Items[1];
        var otherText = otherTab.Document.Text;

        var shortcut = window.InputBindings.OfType<KeyBinding>()
            .Single(binding => binding.Key == Key.H && binding.Modifiers == ModifierKeys.Control);
        var command = (RoutedCommand)shortcut.Command;
        editor.Select(original.Length, "Tiếng Việt".Length);
        Assert(command.CanExecute(null, editor.TextArea), "Ctrl+H is available from the editor");
        command.Execute(null, editor.TextArea);
        var replaceRow = (FrameworkElement)window.FindName("ReplaceRow");
        var query = (TextBox)window.FindName("FindQuery");
        var replacement = (TextBox)window.FindName("ReplaceQuery");
        var replaceOne = (Button)window.FindName("ReplaceOneButton");
        var replaceAll = (Button)window.FindName("ReplaceAllButton");
        var message = (TextBlock)window.FindName("FindMessage");
        Assert(replaceRow.Visibility == Visibility.Visible && query.Text == "Tiếng Việt",
            "Ctrl+H opens replace and prefills selected text");

        replacement.Text = "Đã đổi";
        replaceOne.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(editor.Text == original + "Đã đổi | TIẾNG VIỆT | tiếng việt" &&
            editor.SelectedText == "TIẾNG VIỆT", "Replace one changes the selection and finds the next match");
        editor.Undo();
        Assert(editor.Text == source, "Undo restores the one replacement");

        replacement.Text = "";
        replaceAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(editor.Text == source.Replace(query.Text, "", StringComparison.OrdinalIgnoreCase) &&
            message.Text == "Đã thay 4 chỗ." && otherTab.Document.Text == otherText,
            "Replace All deletes case-insensitive matches in the current tab only");
        Assert(editor.Undo() && editor.Text == source, "One undo restores all replacements");
        Assert(editor.Redo() && !editor.Text.Contains(query.Text, StringComparison.OrdinalIgnoreCase),
            "One redo reapplies all replacements");
        replaceAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(message.Text == "Không tìm thấy", "Replace All reports when there are no matches");
        editor.Undo();
        editor.Undo();
        Assert(editor.Text == original, "Replacement preserves Markdown source outside matched text");
        editor.Select(0, 0);
        ((Button)window.FindName("CloseFindButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Console.WriteLine("[PASS] Ctrl+H, replace one / all, delete matches, current-tab scope and grouped undo");
    }

    private static async Task ExerciseSessionRestoreAsync(MainWindow window, string folder,
        string markdown, string json, string extra, string expectedMarkdown)
    {
        var store = new SettingsService(Path.Combine(folder, "settings.json"));
        var tabs = (ListBox)window.FindName("Tabs");
        var editor = (TextEditor)window.FindName("Editor");
        var wrap = (MenuItem)window.FindName("WrapItem");
        wrap.IsChecked = false;
        wrap.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var largeMarkdown = Path.Combine(folder, "large.md");
        SelectFileTab(tabs, largeMarkdown);
        var markdownPosition = await MoveViewportAsync(window, editor, caretLine: 108, topLine: 601, horizontal: 0);
        SelectFileTab(tabs, extra);
        var textPosition = await MoveViewportAsync(window, editor, caretLine: 126, topLine: 110, horizontal: 180);

        ((Button)window.FindName("NewFileButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SelectFileTab(tabs, markdown);
        var activeCaret = editor.Document.GetLineByNumber(4).Offset + 1;
        editor.CaretOffset = activeCaret;
        var order = tabs.Items.OfType<EditorTab>().Select(tab => (tab.FilePath, tab.Name)).ToArray();
        var recent = store.Load().RecentFiles;
        await CloseWindowAsync(window);

        window = await ReopenWindowAsync(store);
        tabs = (ListBox)window.FindName("Tabs");
        editor = (TextEditor)window.FindName("Editor");
        Assert(tabs.Items.OfType<EditorTab>().Select(tab => (tab.FilePath, tab.Name)).SequenceEqual(order),
            "Restart restores tab order and the blank untitled tab");
        Assert((tabs.SelectedItem as EditorTab)?.FilePath == markdown && editor.Text == expectedMarkdown,
            "Restart selects the previous active tab, even when it is not the last tab");
        Assert(editor.CaretOffset == activeCaret, "Restart restores the active tab's caret");
        SelectFileTab(tabs, extra);
        await CheckViewportAsync(window, editor, textPosition, "Restart restores the TXT caret and both scroll axes");
        SelectFileTab(tabs, largeMarkdown);
        await CheckViewportAsync(window, editor, markdownPosition, "Restart restores the Markdown caret and top line");
        var view = editor.TextArea.TextView;
        await ObserveAsync(window, () => view.GetVisualLine(601)?.Elements.Any(e => e.DocumentLength > e.VisualLength) == true,
            "Markdown formats the restored viewport");
        await CheckViewportAsync(window, editor, markdownPosition, "Markdown formatting leaves the restored view anchored");
        SelectFileTab(tabs, markdown);
        Assert(store.Load().RecentFiles.SequenceEqual(recent), "Restoring tabs does not reorder Recent Files");
        await CloseWindowAsync(window);

        File.Delete(markdown);
        window = await ReopenWindowAsync(store);
        tabs = (ListBox)window.FindName("Tabs");
        Assert(tabs.Items.Count == order.Length - 1 && (tabs.SelectedItem as EditorTab)?.FilePath == json,
            "Restart skips a missing selected file and selects the next available tab");
        var notice = (TextBlock)window.FindName("NoticeText");
        Assert(notice.Text.Contains(Path.GetFileName(markdown), StringComparison.Ordinal), "Missing file is reported");
        await CloseWindowAsync(window);

        window = await ReopenWindowAsync(store, extra);
        tabs = (ListBox)window.FindName("Tabs");
        Assert(tabs.Items.Count == order.Length - 1 && (tabs.SelectedItem as EditorTab)?.FilePath == extra,
            "An explicit launch file takes precedence without duplicating a restored tab");
        await CloseWindowAsync(window);
        Console.WriteLine("[PASS] Closing and reopening restores tab order, active tab and blank tabs, with fallback for missing files");
        Console.WriteLine("[PASS] Restart restores distinct caret and scroll positions in TXT and Markdown");
    }

    private static void SelectFileTab(ListBox tabs, string path) =>
        tabs.SelectedItem = tabs.Items.OfType<EditorTab>().Single(tab => tab.FilePath == path);

    private static async Task<(int Caret, int Top, double Clip, double X)> MoveViewportAsync(
        Window window, TextEditor editor, int caretLine, int topLine, double horizontal)
    {
        var view = editor.TextArea.TextView;
        editor.CaretOffset = editor.Document.GetLineByNumber(caretLine).Offset + 1;
        const double clip = 3;
        editor.ScrollToVerticalOffset(view.GetVisualTopByDocumentLine(topLine) + clip);
        editor.ScrollToHorizontalOffset(horizontal);
        var position = (editor.CaretOffset, topLine, clip, horizontal);
        await CheckViewportAsync(window, editor, position, "Fixture scrolls to its requested position");
        return position;
    }

    private static Task CheckViewportAsync(Window window, TextEditor editor,
        (int Caret, int Top, double Clip, double X) expected, string label)
    {
        var view = editor.TextArea.TextView;
        return ObserveAsync(window, () =>
            editor.CaretOffset == expected.Caret &&
            view.GetDocumentLineByVisualTop(view.VerticalOffset).LineNumber == expected.Top &&
            Math.Abs(view.VerticalOffset - view.GetVisualTopByDocumentLine(expected.Top) - expected.Clip) < 1 &&
            Math.Abs(view.ScrollOffset.X - expected.X) < 1, label);
    }

    private static async Task<MainWindow> ReopenWindowAsync(SettingsService store, params string[] launchPaths)
    {
        var window = new MainWindow(store, store.Load());
        Application.Current.MainWindow = window;
        window.Show();
        await window.StartAsync(launchPaths);
        return window;
    }

    private static async Task CloseWindowAsync(MainWindow window)
    {
        window.Close();
        await ObserveAsync(window, () => !window.IsVisible, "Clean window closes");
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
