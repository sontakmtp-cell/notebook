using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using PromptNotebook.Models;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    private const StringComparison FindComparison = StringComparison.OrdinalIgnoreCase;

    private void FindClick(object sender, RoutedEventArgs e) => OpenFind(showReplace: false);

    private void ReplaceCommandExecuted(object sender, ExecutedRoutedEventArgs e) => OpenFind(showReplace: true);

    private void OpenFind(bool showReplace)
    {
        var selected = Editor.SelectedText;
        if (selected.Length is > 0 and <= 200 && !selected.Contains('\n') && !selected.Contains('\r'))
            FindQuery.Text = selected;
        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = showReplace ? Visibility.Visible : Visibility.Collapsed;
        FindMessage.Text = "";
        FindQuery.Focus();
        FindQuery.SelectAll();
    }

    private async void FindNextClick(object sender, RoutedEventArgs e) => await FindTextAsync(false);
    private async void FindPreviousClick(object sender, RoutedEventArgs e) => await FindTextAsync(true);
    private void CloseFindClick(object sender, RoutedEventArgs e) => CloseFind();

    private void FindQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = FindTextAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void ReplaceQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = ReplaceOneAsync();
    }

    private async void ReplaceOneClick(object sender, RoutedEventArgs e)
    {
        if (await ReplaceOneAsync())
            Editor.Focus();
    }

    private async void ReplaceAllClick(object sender, RoutedEventArgs e)
    {
        if (await ReplaceAllAsync())
            Editor.Focus();
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private async Task<bool> FindTextAsync(bool backwards)
    {
        var query = FindQuery.Text;
        if (query.Length == 0 || _active is not { } tab)
            return false;

        var offset = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        var start = backwards ? offset : offset + Editor.SelectionLength;
        var snapshot = tab.Document.CreateSnapshot();
        var match = snapshot.TextLength >= 256_000
            ? await Task.Run(() => IndexOf(snapshot.Text, query, start, backwards))
            : IndexOf(snapshot.Text, query, start, backwards);
        if (!IsCurrentSearch(tab, snapshot.Version, query))
            return false;

        FindMessage.Text = match < 0 ? "Không tìm thấy" : "";
        if (match < 0)
        {
            FindBar.Visibility = Visibility.Visible;
            return false;
        }

        Editor.Select(match, query.Length);
        Editor.TextArea.Caret.BringCaretToView();
        return true;
    }

    private async Task<bool> ReplaceOneAsync()
    {
        var query = FindQuery.Text;
        var replacement = ReplaceQuery.Text;
        if (query.Length == 0 || _active is not { } tab || Editor.IsReadOnly)
            return false;

        if (!string.Equals(Editor.SelectedText, query, FindComparison) && !await FindTextAsync(false))
            return false;
        if (!ReferenceEquals(_active, tab) || Editor.IsReadOnly || FindQuery.Text != query ||
            ReplaceQuery.Text != replacement || !string.Equals(Editor.SelectedText, query, FindComparison))
            return false;

        var offset = Editor.SelectionStart;
        if (!string.Equals(Editor.SelectedText, replacement, StringComparison.Ordinal))
            Editor.SelectedText = replacement;
        Editor.Select(offset + replacement.Length, 0);
        await FindTextAsync(false);
        if (ReferenceEquals(_active, tab) && FindQuery.Text == query)
            FindMessage.Text = "Đã thay 1 chỗ.";
        return ReferenceEquals(_active, tab);
    }

    private async Task<bool> ReplaceAllAsync()
    {
        var query = FindQuery.Text;
        var replacement = ReplaceQuery.Text;
        if (query.Length == 0 || _active is not { } tab || Editor.IsReadOnly)
            return false;

        var snapshot = tab.Document.CreateSnapshot();
        var (text, count) = snapshot.TextLength >= 256_000
            ? await Task.Run(() => ReplaceMatches(snapshot.Text, query, replacement))
            : ReplaceMatches(snapshot.Text, query, replacement);
        if (!IsCurrentSearch(tab, snapshot.Version, query) || Editor.IsReadOnly || ReplaceQuery.Text != replacement)
            return false;
        if (count == 0)
        {
            FindMessage.Text = "Không tìm thấy";
            return false;
        }

        if (text is not null)
        {
            var caret = Editor.CaretOffset;
            tab.Document.Replace(0, tab.Document.TextLength, text);
            Editor.Select(Math.Min(caret, text.Length), 0);
            Editor.TextArea.Caret.BringCaretToView();
        }
        FindMessage.Text = $"Đã thay {count:N0} chỗ.";
        return true;
    }

    private bool IsCurrentSearch(EditorTab tab, ITextSourceVersion? version, string query) =>
        ReferenceEquals(_active, tab) && ReferenceEquals(version, tab.Document.Version) && FindQuery.Text == query;

    private static int IndexOf(string text, string query, int offset, bool backwards)
    {
        if (backwards)
        {
            var previous = text.AsSpan(0, offset).LastIndexOf(query, FindComparison);
            return previous >= 0 ? previous : text.LastIndexOf(query, FindComparison);
        }
        var next = text.IndexOf(query, offset, FindComparison);
        return next >= 0 ? next : text.IndexOf(query, FindComparison);
    }

    private static (string? Text, int Count) ReplaceMatches(string text, string query, string replacement)
    {
        var count = 0;
        for (var index = text.IndexOf(query, FindComparison); index >= 0;
            index = text.IndexOf(query, index + query.Length, FindComparison))
            count++;
        if (count == 0)
            return (null, 0);
        var replaced = text.Replace(query, replacement, FindComparison);
        return (replaced == text ? null : replaced, count);
    }
}
