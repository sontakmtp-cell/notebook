using System.IO;
using PromptNotebook.Models;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    private void CaptureSession()
    {
        if (_active is { } active)
            RememberView(active);
        _settings.LastSession = _tabs.Count == 1 && EmptyPlaceholder is not null ? null :
            new EditorSession(
                _tabs.Select(CaptureTab).ToArray(),
                Tabs.SelectedIndex,
                _untitledCount);
    }

    private static SessionTab CaptureTab(EditorTab tab)
    {
        var caret = tab.Document.GetLocation(Math.Clamp(tab.Caret, 0, tab.Document.TextLength));
        return new SessionTab(tab.FilePath, tab.Name, tab.Format, new TabViewState(caret.Line, caret.Column, tab.Viewport));
    }

    private async Task<List<string>> RestoreSessionAsync()
    {
        var errors = new List<string>();
        if (_settings.LastSession is not { Tabs.Length: > 0 } session)
            return errors;

        var placeholder = EmptyPlaceholder;
        _untitledCount = Math.Max(_untitledCount, session.UntitledCount);
        var restored = new EditorTab?[session.Tabs.Length];
        for (var index = 0; index < session.Tabs.Length; index++)
        {
            var saved = session.Tabs[index];
            if (saved is null)
                continue;

            if (saved.FilePath is { } path && Path.IsPathFullyQualified(path))
            {
                try
                {
                    restored[index] = await OpenPathAsync(path, restoring: true);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    errors.Add($"Không thể khôi phục «{Path.GetFileName(path)}». {ex.Message}");
                }
            }
            else if (saved.FilePath is null && !string.IsNullOrWhiteSpace(saved.Name) && Enum.IsDefined(saved.Format))
            {
                var tab = new EditorTab(Path.GetFileNameWithoutExtension(saved.Name), saved.Format);
                AddTab(tab, select: false);
                restored[index] = tab;
            }

            if (restored[index] is { } tabToRestore && saved.View is { } view)
            {
                var document = tabToRestore.Document;
                var line = document.GetLineByNumber(Math.Clamp(view.CaretLine, 1, document.LineCount));
                var column = Math.Clamp(view.CaretColumn, 1, line.Length + 1);
                tabToRestore.Caret = line.Offset + column - 1;
                tabToRestore.SelectionAnchor = tabToRestore.SelectionEnd = tabToRestore.Caret;
                tabToRestore.Viewport = view.Viewport;
            }
        }

        var selected = Math.Clamp(session.SelectedIndex, 0, restored.Length - 1);
        var target = restored.Skip(selected).FirstOrDefault(tab => tab is not null) ??
            restored.Take(selected).LastOrDefault(tab => tab is not null);
        if (target is not null)
        {
            SelectTab(target);
            if (placeholder is not null && ReferenceEquals(EmptyPlaceholder, placeholder))
                RemoveTab(placeholder);
        }
        return errors;
    }
}
