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
            return;
        }

        var file = await TextFiles.ReadAsync(path);
        var placeholder = _tabs.Count == 1 && _active is { FilePath: null, IsDirty: false, Format: FileFormat.Txt } current &&
            current.Document.TextLength == 0 ? current : null;
        AddTab(new EditorTab(Path.GetFileNameWithoutExtension(path), format, path, file.Text, file.Encoding));
        if (placeholder is not null)
            RemoveTab(placeholder);
    }

    private EditorTab? FindTab(string path) => _tabs.FirstOrDefault(tab =>
        string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase));

    private async void SaveClick(object sender, RoutedEventArgs e) => await SaveActiveAsync(false);
    private async void SaveAsClick(object sender, RoutedEventArgs e) => await SaveActiveAsync(true);

    private Task SaveActiveAsync(bool saveAs) => RunFileOperationAsync(async () =>
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
            if (path is not null)
                dialog.InitialDirectory = Path.GetDirectoryName(path);
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

        var snapshot = tab.Document.CreateSnapshot();
        await TextFiles.WriteAsync(path, snapshot.Text, tab.Encoding);
        var changedFormat = tab.Format != format;
        tab.CompleteSave(path, snapshot.Version!);
        if (changedFormat && ReferenceEquals(_active, tab))
            ApplyFormat();
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
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;
}
