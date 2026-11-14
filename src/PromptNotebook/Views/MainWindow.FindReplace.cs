using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    // Tìm chữ thường, không phân biệt hoa/thường, chỉ trong prompt đang mở.
    private const StringComparison FindComparison = StringComparison.OrdinalIgnoreCase;

    private void OpenFindBar(bool showReplace)
    {
        if (_viewModel.IsPreviewMode) _viewModel.EditorViewMode = "Split";
        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = showReplace ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(ContentEditorTextBox.SelectedText))
            FindTextBox.Text = ContentEditorTextBox.SelectedText;
        UpdateFindStatus();
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void CloseFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ContentEditorTextBox.Focus();
    }

    private void UpdateFindStatus()
    {
        if (FindBar?.Visibility != Visibility.Visible || ContentEditorTextBox is null) return;
        var count = CountMatches(ContentEditorTextBox.Text, FindTextBox.Text);
        FindStatusText.Text = string.IsNullOrEmpty(FindTextBox.Text)
            ? "Tìm trong prompt này · Không phân biệt hoa/thường."
            : count == 0 ? "Không tìm thấy trong prompt này." : $"{count:N0} kết quả trong prompt này.";
    }

    private static int CountMatches(string text, string query)
    {
        if (query.Length == 0) return 0;
        var count = 0;
        for (var index = text.IndexOf(query, FindComparison); index >= 0;
             index = text.IndexOf(query, index + query.Length, FindComparison))
            count++;
        return count;
    }

    private bool FindMatch(bool backwards = false, bool fromStart = false)
    {
        var query = FindTextBox.Text;
        if (!_viewModel.HasSelectedPrompt || query.Length == 0) return false;
        if (_viewModel.IsPreviewMode) _viewModel.EditorViewMode = "Split";

        var editor = ContentEditorTextBox;
        var text = editor.Text;
        int index;
        if (backwards)
        {
            var start = editor.SelectionStart - 1;
            index = start >= 0 ? text.LastIndexOf(query, start, FindComparison) : -1;
            if (index < 0) index = text.LastIndexOf(query, FindComparison);
        }
        else
        {
            var start = fromStart ? 0 : editor.SelectionStart + editor.SelectionLength;
            index = text.IndexOf(query, start, FindComparison);
            if (index < 0) index = text.IndexOf(query, FindComparison);
        }

        UpdateFindStatus();
        if (index < 0) return false;
        editor.Select(index, query.Length);
        // Hiện vùng soạn thảo trước khi tính dòng, kể cả đang ở chế độ xem trước.
        editor.UpdateLayout();
        var line = editor.GetLineIndexFromCharacterIndex(index);
        if (line >= 0) editor.ScrollToLine(line);
        return true;
    }

    private void ReplaceCurrentMatch()
    {
        if (!_viewModel.IsEditorEnabled || FindTextBox.Text.Length == 0) return;
        var editor = ContentEditorTextBox;
        if (!string.Equals(editor.SelectedText, FindTextBox.Text, FindComparison) && !FindMatch()) return;

        var start = editor.SelectionStart;
        var oldLength = editor.Text.Length;
        var matchedLength = editor.SelectionLength;
        // SelectedText tự tạo một bước hoàn tác; đọc Text sau khi bước thay kết thúc.
        editor.SelectedText = ReplaceTextBox.Text;
        var insertedLength = editor.Text.Length - oldLength + matchedLength;
        editor.Select(start + insertedLength, 0);
        FindMatch();
        FindStatusText.Text = "Đã thay 1 chỗ. Ctrl+Z trong khung soạn thảo để hoàn tác.";
    }

    private void ReplaceAllMatches()
    {
        if (!_viewModel.IsEditorEnabled || FindTextBox.Text.Length == 0) return;
        var editor = ContentEditorTextBox;
        var count = CountMatches(editor.Text, FindTextBox.Text);
        if (count == 0) { UpdateFindStatus(); return; }

        var replaced = editor.Text.Replace(FindTextBox.Text, ReplaceTextBox.Text, FindComparison);
        var caret = editor.SelectionStart;
        // Một lần hoàn tác khôi phục toàn bộ thao tác "Thay tất cả".
        editor.BeginChange();
        try
        {
            editor.SelectAll();
            editor.SelectedText = replaced;
        }
        finally { editor.EndChange(); }
        editor.Select(Math.Min(caret, editor.Text.Length), 0);
        FindStatusText.Text = $"Đã thay {count:N0} chỗ. Ctrl+Z trong khung soạn thảo để hoàn tác.";
    }

    private void OpenFindReplaceButton_Click(object sender, RoutedEventArgs e) => OpenFindBar(showReplace: true);
    private void CloseFindBarButton_Click(object sender, RoutedEventArgs e) => CloseFindBar();
    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindMatch();
    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) => FindMatch(backwards: true);
    private void ReplaceButton_Click(object sender, RoutedEventArgs e) => ReplaceCurrentMatch();
    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => ReplaceAllMatches();

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FindBar?.Visibility != Visibility.Visible) return;
        if (FindTextBox.Text.Length == 0) UpdateFindStatus();
        else FindMatch(fromStart: true);
    }

    private void ContentEditorTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateFindStatus();

    private void FindTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FindMatch(backwards: Keyboard.Modifiers == ModifierKeys.Shift);
        e.Handled = true;
    }

    private void ReplaceTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ReplaceCurrentMatch();
        e.Handled = true;
    }
}
