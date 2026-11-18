using System.Windows;
using System.Windows.Input;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    private void FindClick(object sender, RoutedEventArgs e)
    {
        var selected = Editor.SelectedText;
        if (selected.Length is > 0 and <= 200 && !selected.Contains('\n') && !selected.Contains('\r'))
            FindQuery.Text = selected;
        FindBar.Visibility = Visibility.Visible;
        FindMessage.Text = "";
        FindQuery.Focus();
        FindQuery.SelectAll();
    }

    private void FindNextClick(object sender, RoutedEventArgs e) => FindText(false);
    private void FindPreviousClick(object sender, RoutedEventArgs e) => FindText(true);
    private void CloseFindClick(object sender, RoutedEventArgs e) => CloseFind();

    private void FindQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        FindText(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private async void FindText(bool backwards)
    {
        var query = FindQuery.Text;
        if (query.Length == 0 || _active is not { } tab)
            return;

        var offset = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        var start = backwards ? offset : offset + Editor.SelectionLength;
        var snapshot = tab.Document.CreateSnapshot();
        var match = snapshot.TextLength >= 256_000
            ? await Task.Run(() => IndexOf(snapshot.Text, query, start, backwards))
            : IndexOf(snapshot.Text, query, start, backwards);
        if (!ReferenceEquals(_active, tab) || !ReferenceEquals(snapshot.Version, tab.Document.Version) ||
            !string.Equals(query, FindQuery.Text, StringComparison.Ordinal))
            return;

        FindMessage.Text = match < 0 ? "Không tìm thấy" : "";
        if (match < 0)
        {
            FindBar.Visibility = Visibility.Visible;
            return;
        }
        Editor.CaretOffset = match + query.Length;
        Editor.Select(match, query.Length);
        Editor.TextArea.Caret.BringCaretToView();
    }

    private static int IndexOf(string text, string query, int offset, bool backwards)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (backwards)
        {
            var previous = text.AsSpan(0, offset).LastIndexOf(query, comparison);
            return previous >= 0 ? previous : text.LastIndexOf(query, comparison);
        }
        var next = text.IndexOf(query, offset, comparison);
        return next >= 0 ? next : text.IndexOf(query, comparison);
    }
}
