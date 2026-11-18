using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using PromptNotebook.Models;
using PromptNotebook.Services;

namespace PromptNotebook.Views;

public partial class MainWindow
{
    private const string OpenFilter = "Tệp được hỗ trợ (*.txt;*.json;*.md)|*.txt;*.json;*.md|" + SaveFilter;
    private const string SaveFilter = "Văn bản (*.txt)|*.txt|JSON (*.json)|*.json|Markdown (*.md)|*.md";

    private readonly Queue<string[]> _externalOpens = new();
    private bool _fileOperation;
    private bool _allowWindowClose;

    private void AddNew(FileFormat format = FileFormat.Txt) =>
        AddTab(new EditorTab($"Không tên {++_untitledCount}", format));

    private void NewJsonClick(object sender, RoutedEventArgs e) => AddNew(FileFormat.Json);
    private void NewMarkdownClick(object sender, RoutedEventArgs e) => AddNew(FileFormat.Markdown);

    private async void OpenClick(object sender, RoutedEventArgs e) => await RunFileOperationAsync(async () =>
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = OpenFilter, Title = "Mở tệp" };
        if (dialog.ShowDialog(this) == true)
            await OpenPathsAsync(dialog.FileNames);
    });

    public Task OpenFromStartupAsync(IEnumerable<string> paths) =>
        RunFileOperationAsync(() => OpenPathsAsync(paths));

    internal Task StartAsync(IEnumerable<string> paths) => RunFileOperationAsync(async () =>
    {
        await RestoreDraftsAsync();
        await OpenPathsAsync(paths);
    });

    internal bool QueueExternalOpen(string[] paths)
    {
        if (_ending)
            return false;
        _externalOpens.Enqueue(paths);
        _ = Dispatcher.InvokeAsync(ProcessExternalOpen);
        return true;
    }

    private void ProcessExternalOpen()
    {
        if (_fileOperation || _ending || !_externalOpens.TryDequeue(out var paths))
            return;

        _ = RunFileOperationAsync(async () =>
        {
            if (WindowState == WindowState.Minimized)
                SystemCommands.RestoreWindow(this);
            Activate();
            await OpenPathsAsync(paths);
        });
    }

    private void FilesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        e.Effects = _fileOperation ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void FilesDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;
        e.Handled = true;
        await OpenFromStartupAsync(paths);
    }

    private async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                await OpenPathAsync(path);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            var summary = string.Join(Environment.NewLine, errors.Take(4));
            if (errors.Count > 4)
                summary += $"\nVà {errors.Count - 4} tệp khác.";
            ShowNotice(summary, true);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        path = Path.GetFullPath(path);
        var format = FileFormats.FromPath(path);
        var alreadyOpen = FindTab(path);
        if (alreadyOpen is not null)
        {
            Tabs.SelectedItem = alreadyOpen;
            Tabs.ScrollIntoView(alreadyOpen);
            Editor.Focus();
            RememberFile(path);
            return;
        }

        var (document, encoding, characters, stamp) = await Task.Run(async () =>
        {
            var before = FileStamp.Read(path);
            var loaded = await TextFiles.ReadAsync(path).ConfigureAwait(false);
            var after = FileStamp.Read(path);
            if (after is null || before != after)
                throw new IOException("Tệp thay đổi trong lúc đang mở. Hãy thử mở lại.");
            var textDocument = new TextDocument(loaded.Text);
            textDocument.SetOwnerThread(null);
            return (textDocument, loaded.Encoding, EditorTab.CountCharacters(loaded.Text), after.Value);
        });
        var placeholder = _tabs.Count == 1 && _active is { FilePath: null, IsDirty: false, Format: FileFormat.Txt } current &&
            current.Document.TextLength == 0 ? current : null;
        document.SetOwnerThread(Thread.CurrentThread);
        AddTab(new EditorTab(Path.GetFileNameWithoutExtension(path), format, path, document, encoding, characters)
        {
            SavedStamp = stamp
        });
        if (placeholder is not null)
            RemoveTab(placeholder);
        RememberFile(path);
    }

    private EditorTab? FindTab(string path) => _tabs.FirstOrDefault(tab =>
        string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase));

    private async void SaveClick(object sender, RoutedEventArgs e) => await SaveActiveAsync(false);
    private async void SaveAsClick(object sender, RoutedEventArgs e) => await SaveActiveAsync(true);
    private async void SaveAllClick(object sender, RoutedEventArgs e) => await SaveAllAsync();

    internal Task SaveAllAsync() => RunFileOperationAsync(async () =>
    {
        var saved = 0;
        foreach (var tab in _tabs.Where(tab => tab.IsDirty).ToArray())
        {
            if (!await SaveTabAsync(tab))
                return;
            saved++;
        }

        if (saved > 0)
            ShowNotice($"Đã lưu {saved} tệp.");
    });

    internal Task SaveActiveAsync(bool saveAs) => RunFileOperationAsync(async () =>
    {
        if (_active is { } tab)
            await SaveTabAsync(tab, saveAs);
    });

    private async Task<bool> SaveTabAsync(EditorTab tab, bool saveAs = false)
    {
        var path = tab.FilePath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog
            {
                FileName = tab.Name,
                DefaultExt = tab.Format.Extension(),
                Filter = SaveFilter,
                FilterIndex = (int)tab.Format + 1,
                AddExtension = true,
                OverwritePrompt = true,
                Title = "Lưu tệp"
            };
            if ((path ?? tab.RecoveredFrom) is { } origin)
                dialog.InitialDirectory = Path.GetDirectoryName(origin);
            if (dialog.ShowDialog(this) != true)
                return false;
            path = Path.GetFullPath(dialog.FileName);
        }

        var format = FileFormats.FromPath(path);
        if (FindTab(path) is { } existing && !ReferenceEquals(tab, existing))
        {
            ShowNotice("Tệp đích đã mở trong tab khác.", true);
            return false;
        }

        var disk = await Task.Run(() => FileStamp.Read(path));
        if (!saveAs && tab.FilePath is not null && disk != tab.SavedStamp &&
            MessageBox.Show(this, $"«{tab.Name}» đã thay đổi trên đĩa. Ghi nội dung trong Notebook vào tệp này?",
                "Notebook", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return false;

        var snapshot = tab.Document.CreateSnapshot();
        tab.SavedStamp = await Task.Run(() => TextFiles.WriteCheckedAsync(path, snapshot.Text, tab.Encoding, disk));
        var changedPresentation = tab.Format != format ||
            (format == FileFormat.Markdown && tab.FilePath != path);
        tab.CompleteSave(path, snapshot.Version!);
        if (changedPresentation && ReferenceEquals(_active, tab))
            ApplyFormat();
        if (!tab.IsDirty)
        {
            _draftVersions.Remove(tab);
            await RemoveDraftAsync(tab);
        }
        RememberFile(path);
        return true;
    }

    private Task CloseTabAsync(EditorTab tab) => RunFileOperationAsync(async () =>
    {
        Editor.IsReadOnly = true;
        try
        {
            if (await ConfirmCloseAsync(tab))
            {
                RemoveTab(tab);
                if (_tabs.Count == 0)
                    AddNew();
                await RemoveDraftAsync(tab);
                Editor.Focus();
            }
        }
        finally
        {
            Editor.IsReadOnly = false;
        }
    });

    private async Task<bool> ConfirmCloseAsync(EditorTab tab)
    {
        if (!tab.IsDirty)
            return true;

        return MessageBox.Show(this, $"Lưu thay đổi trong «{tab.Name}» trước khi đóng?\n" +
            "Chọn No (Không) để bỏ thay đổi, hoặc Cancel (Hủy) để tiếp tục chỉnh sửa.",
            "Notebook", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes) switch
        {
            MessageBoxResult.Yes => await SaveTabAsync(tab),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private async Task RunFileOperationAsync(Func<Task> action)
    {
        if (_fileOperation)
            return;
        _fileOperation = true;
        try
        {
            await action();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ShowNotice($"Không thể hoàn tất thao tác. {ex.Message}", true);
        }
        finally
        {
            _fileOperation = false;
            if (_externalOpens.Count > 0)
                _ = Dispatcher.InvokeAsync(ProcessExternalOpen);
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private void RememberFile(string path)
    {
        _settings.RecentFiles.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveAt(10);
        _settingsService.Save(_settings);
    }

    private void RecentClick(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        var menu = button.ContextMenu;
        menu.Items.Clear();
        foreach (var path in _settings.RecentFiles)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = new System.Windows.Controls.TextBlock { Text = Path.GetFileName(path) },
                ToolTip = path
            };
            item.Click += async (_, _) => await OpenFromStartupAsync([path]);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Chưa có tệp gần đây", IsEnabled = false });
        SettingsClick(sender, e);
    }
}
