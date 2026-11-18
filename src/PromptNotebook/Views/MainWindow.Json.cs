using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using PromptNotebook.Helpers;
using PromptNotebook.Markdown;
using PromptNotebook.Models;
using PromptNotebook.Services;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    private readonly DefaultIndentationStrategy _defaultIndentation = new();
    private static readonly FontFamily MarkdownFont = new("Segoe UI");
    private static readonly FontFamily PlainFont = new("Cascadia Mono, Consolas");

    private void AlignJsonClosingBracket(object? sender, TextCompositionEventArgs e)
    {
        if (Editor.IsReadOnly || _active?.Format != FileFormat.Json ||
            e.Text is not ("}" or "]") || Editor.SelectionLength != 0)
            return;

        var offset = Editor.CaretOffset;
        var dedent = JsonIndentation.ClosingBracketDedent(Editor.Document, offset);
        if (dedent == 0)
            return;

        Editor.Document.Replace(offset - dedent, dedent, e.Text);
        Editor.CaretOffset = offset - dedent + 1;
        Editor.TextArea.Caret.BringCaretToView();
        e.Handled = true;
    }

    private void ApplyFormat()
    {
        var json = _active?.Format == FileFormat.Json;
        Editor.SyntaxHighlighting = json ? JsonHighlighting.ForTheme(_settings.IsDarkMode) : null;
        Editor.TextArea.IndentationStrategy = json ? JsonIndentation.Instance : _defaultIndentation;
        Editor.Options.IndentationSize = json ? 2 : 4;
        JsonTools.Visibility = json ? Visibility.Visible : Visibility.Collapsed;
        var markdown = _active?.Format == FileFormat.Markdown ? _active : null;
        Editor.FontFamily = markdown is null ? PlainFont : MarkdownFont;
        _markdown.Apply(markdown, _settings.IsDarkMode);
    }

    private async void FormatJsonClick(object sender, RoutedEventArgs e) => await RunJsonActionAsync(true);
    private async void MinifyJsonClick(object sender, RoutedEventArgs e) => await RunJsonActionAsync(false);
    private async void ValidateJsonClick(object sender, RoutedEventArgs e) => await RunJsonActionAsync(null);

    internal async Task RunJsonActionAsync(bool? indented)
    {
        if (Editor.IsReadOnly || _active is not { Format: FileFormat.Json } tab)
            return;

        var snapshot = tab.Document.CreateSnapshot();
        var eol = TextUtilities.GetNewLineFromDocument(tab.Document, 1);
        try
        {
            var (source, result) = await Task.Run(() =>
            {
                var text = snapshot.Text;
                if (indented is { } flag)
                    return (text, JsonEditing.Format(text, flag, eol));
                using var parsed = JsonDocument.Parse(text);
                return (text, text);
            });

            if (!ReferenceEquals(_active, tab) || !ReferenceEquals(snapshot.Version, tab.Document.Version))
                return;
            if (source != result)
            {
                var caret = Editor.CaretOffset;
                tab.Document.Replace(0, tab.Document.TextLength, result);
                Editor.CaretOffset = Math.Min(caret, result.Length);
            }
            ShowNotice(indented switch
            {
                true => "Đã định dạng JSON.",
                false => "Đã thu gọn JSON.",
                null => "Cú pháp JSON hợp lệ."
            });
            Editor.Focus();
        }
        catch (JsonException ex)
        {
            if (!ReferenceEquals(_active, tab) || !ReferenceEquals(snapshot.Version, tab.Document.Version))
                return;
            var row = (int)Math.Clamp(ex.LineNumber.GetValueOrDefault() + 1, 1, tab.Document.LineCount);
            var line = tab.Document.GetLineByNumber(row);
            var text = tab.Document.GetText(line);
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var byteIndex = (int)Math.Clamp(ex.BytePositionInLine.GetValueOrDefault(), 0, bytes.Length);
            var column = System.Text.Encoding.UTF8.GetCharCount(bytes.AsSpan(0, byteIndex)) + 1;
            ShowNotice($"JSON sai cú pháp tại dòng {row}, cột {column}. Hãy kiểm tra dấu ngoặc, dấu phẩy và dấu ngoặc kép tại vị trí này.", true);
            Editor.CaretOffset = Math.Min(line.Offset + column - 1, line.EndOffset);
            Editor.ScrollTo(row, column);
            Editor.Focus();
        }
        catch (ArgumentException)
        {
            if (ReferenceEquals(_active, tab) && ReferenceEquals(snapshot.Version, tab.Document.Version))
                ShowNotice("JSON chứa chuỗi Unicode không hợp lệ.", true);
        }
        catch (InvalidOperationException)
        {
            if (ReferenceEquals(_active, tab) && ReferenceEquals(snapshot.Version, tab.Document.Version))
                ShowNotice("JSON chứa chuỗi Unicode không hợp lệ.", true);
        }
    }

    private void ShowNotice(string message, bool isError = false)
    {
        NoticeText.Text = message;
        NoticeText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            isError ? "DangerBrush" : "AccentBrush");
        NoticeBar.Visibility = Visibility.Visible;
    }

    private void ClearNoticeClick(object sender, RoutedEventArgs e) => NoticeBar.Visibility = Visibility.Collapsed;
}
