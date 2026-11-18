using System.IO;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using PromptNotebook.Models;
using PromptNotebook.Services;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    private readonly RecoveryStore _recovery;
    private readonly DispatcherTimer _draftTimer;
    private readonly HashSet<EditorTab> _pendingDrafts = [];
    private readonly Dictionary<EditorTab, ITextSourceVersion> _draftVersions = [];
    private bool _draftWork;
    private bool _ending;
    private bool _recoveryUnavailable;

    private async Task RestoreDraftsAsync()
    {
        try
        {
            var recovered = await _recovery.RecoverAsync();
            if (recovered.Count == 0)
                return;

            var placeholder = _tabs.Count == 1 ? EmptyPlaceholder : null;
            foreach (var draft in recovered)
            {
                var tab = new EditorTab($"Khôi phục - {Path.GetFileNameWithoutExtension(draft.Name)}",
                    draft.Format, text: draft.Text, encoding: draft.Encoding)
                {
                    RecoveryId = draft.Id,
                    RecoveryName = draft.Name,
                    RecoveredFrom = draft.OriginalPath
                };
                tab.Document.UndoStack.DiscardOriginalFileMarker();
                _draftVersions[tab] = tab.Document.Version;
                AddTab(tab);
            }
            if (placeholder is not null)
                RemoveTab(placeholder);
            ShowNotice($"Đã mở {recovered.Count} bản nháp khôi phục. Dùng Lưu để chọn tệp đích.");
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _recoveryUnavailable = true;
            ShowNotice("Không thể đọc bản nháp khôi phục. Bạn vẫn có thể mở và lưu tệp.", true);
        }
    }

    private void WatchTab(EditorTab tab) => tab.Document.TextChanged += DraftDocumentChanged;
    private void UnwatchTab(EditorTab tab) => tab.Document.TextChanged -= DraftDocumentChanged;

    private void DraftDocumentChanged(object? sender, EventArgs e)
    {
        if (sender is TextDocument document && _tabs.FirstOrDefault(tab => tab.Document == document) is { } tab)
            QueueDraft(tab);
    }

    private void QueueDraft(EditorTab tab)
    {
        if (_ending)
            return;
        _pendingDrafts.Add(tab);
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private async void DraftTick(object? sender, EventArgs e)
    {
        _draftTimer.Stop();
        if (_draftWork || _ending || _pendingDrafts.Count == 0)
            return;

        _draftWork = true;
        var tabs = _pendingDrafts.ToArray();
        _pendingDrafts.Clear();
        try
        {
            foreach (var tab in tabs)
            {
                if (_ending || !_tabs.Contains(tab))
                    continue;

                try
                {
                    if (!tab.IsDirty)
                    {
                        await _recovery.DiscardAsync(tab.RecoveryId);
                        _draftVersions.Remove(tab);
                        continue;
                    }

                    var snapshot = tab.Document.CreateSnapshot();
                    var name = tab.FilePath is null ? tab.RecoveryName ?? tab.Name : tab.Name;
                    await _recovery.KeepAsync(tab.RecoveryId, name, tab.FilePath ?? tab.RecoveredFrom,
                        tab.Format, tab.Encoding, snapshot);
                    if (_tabs.Contains(tab) && !_ending)
                        _draftVersions[tab] = snapshot.Version!;
                    _recoveryUnavailable = false;
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    _recoveryUnavailable = true;
                }
            }
        }
        finally
        {
            _draftWork = false;
            if (!_ending && _pendingDrafts.Count > 0 && !_draftTimer.IsEnabled)
                _draftTimer.Start();
            UpdateStatus();
        }
    }

    private async Task RemoveDraftAsync(EditorTab tab)
    {
        _pendingDrafts.Remove(tab);
        _draftVersions.Remove(tab);
        try
        {
            await _recovery.DiscardAsync(tab.RecoveryId);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            _recoveryUnavailable = true;
        }
    }

    private string DirtyStatus(EditorTab tab)
    {
        if (_recoveryUnavailable)
            return "Chưa lưu · chưa giữ được bản nháp";
        return _draftVersions.TryGetValue(tab, out var version) && ReferenceEquals(version, tab.Document.Version) ?
            "Chưa lưu · đã giữ bản nháp" : "Chưa lưu";
    }
}
