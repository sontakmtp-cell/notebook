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

    private void FindText(bool backwards)
    {
        var query = FindQuery.Text;
        if (query.Length == 0)
            return;

        var text = Editor.Text;
        var offset = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        int match;
        if (backwards)
        {
            match = text.AsSpan(0, offset).LastIndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
                match = text.LastIndexOf(query, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var start = offset + Editor.SelectionLength;
            match = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
                match = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

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
}
