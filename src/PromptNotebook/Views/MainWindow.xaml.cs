using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;

namespace PromptNotebook.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private const string PromptDragFormat = "PromptNotebook.Prompt";
    private Point _dragStart;
    private PromptItem? _draggedPrompt;
    private InsertionAdorner? _insertionAdorner;

    public MainWindow(MainViewModel viewModel, SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _settings = settings;
        _ = settingsService;

        DataContext = _viewModel;

        ApplyWindowGeometryAndColumnRatio(_settings);

        _viewModel.ThemeChanged += (_, isDark) => App.ApplyTheme(isDark);
        _viewModel.FocusTitleRequested += (_, _) =>
        {
            TitleEditorTextBox.Focus();
            TitleEditorTextBox.SelectAll();
        };
        _viewModel.FocusSearchRequested += (_, _) =>
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        };
    }

    private void ApplyWindowGeometryAndColumnRatio(AppSettings s)
    {
        if (s.WindowWidth >= MinWidth)
            Width = s.WindowWidth;

        if (s.WindowHeight >= MinHeight)
            Height = s.WindowHeight;

        if (s.WindowLeft.HasValue && s.WindowTop.HasValue)
        {
            var left = s.WindowLeft.Value;
            var top = s.WindowTop.Value;

            // Đảm bảo cửa sổ nằm trong vùng màn hình hợp lệ
            if (left >= SystemParameters.VirtualScreenLeft - 50 &&
                top >= SystemParameters.VirtualScreenTop - 50 &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (s.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        // Khôi phục tỷ lệ độ rộng hai cột (mặc định 40% - 60%)
        var leftRatio = Math.Clamp(s.LeftColumnWidthRatio, 15.0, 75.0);
        var rightRatio = 100.0 - leftRatio;
        LeftColumn.Width = new GridLength(leftRatio, GridUnitType.Star);
        RightColumn.Width = new GridLength(rightRatio, GridUnitType.Star);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+F -> đưa con trỏ vào thanh tìm kiếm
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Phím Esc -> xóa từ khóa tìm kiếm hoặc bỏ chọn prompt
        if (e.Key == Key.Escape)
        {
            _viewModel.ExecuteEscapeKey();
            e.Handled = true;
            return;
        }

        // Phím Delete -> xóa prompt đang chọn (chỉ kích hoạt khi người dùng không đang gõ chữ trong TextBox/ComboBox)
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not ComboBox)
            {
                if (_viewModel.HasSelectedPrompt)
                {
                    _viewModel.ExecuteDeleteOrRestorePrompt(_viewModel.SelectedPrompt);
                    e.Handled = true;
                }
            }
        }
    }

    private void ItemFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PromptItem item })
        {
            _viewModel.ExecuteToggleFavorite(item);
            e.Handled = true;
        }
    }

    private static PromptItem? GetPromptFromMenuItem(object sender)
    {
        if (sender is MenuItem { DataContext: PromptItem item })
            return item;
        return null;
    }

    private void ContextMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        var item = GetPromptFromMenuItem(sender) ?? _viewModel.SelectedPrompt;
        if (item is null) return;

        _viewModel.FlushPendingSave();

        var dialog = new RenameDialog(item.Title, item.Category)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.RenamePrompt(item, dialog.PromptTitle, dialog.PromptCategory);
        }
    }

    private void ContextMenu_Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var item = GetPromptFromMenuItem(sender) ?? _viewModel.SelectedPrompt;
        if (item is not null)
        {
            _viewModel.ExecuteDuplicatePrompt(item);
        }
    }

    private void ContextMenu_Favorite_Click(object sender, RoutedEventArgs e)
    {
        var item = GetPromptFromMenuItem(sender) ?? _viewModel.SelectedPrompt;
        if (item is not null)
        {
            _viewModel.ExecuteToggleFavorite(item);
        }
    }

    private void ContextMenu_DeleteOrRestore_Click(object sender, RoutedEventArgs e)
    {
        var item = GetPromptFromMenuItem(sender) ?? _viewModel.SelectedPrompt;
        if (item is not null)
        {
            _viewModel.ExecuteDeleteOrRestorePrompt(item);
        }
    }

    private void ContextMenu_PermanentDelete_Click(object sender, RoutedEventArgs e)
    {
        var item = GetPromptFromMenuItem(sender) ?? _viewModel.SelectedPrompt;
        if (item is not null)
        {
            _viewModel.ExecutePermanentDelete(item);
        }
    }

    private void ExportJsonButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất dữ liệu Prompt ra file JSON UTF-8",
            Filter = "JSON Files (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"PromptNotebook_Export_{DateTime.Now:yyyyMMdd_HHmm}.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var count = _viewModel.ExportJson(dialog.FileName);
                MessageBox.Show(
                    this,
                    $"Đã xuất thành công {count:N0} prompt ra file:\n{dialog.FileName}",
                    "Xuất JSON thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Lỗi khi xuất file JSON:\n{ex.Message}",
                    "Lỗi xuất dữ liệu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async void ImportFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanImportTextFiles)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Nhập prompt từ file TXT, Markdown hoặc JSON",
            Filter = "File prompt (*.txt;*.md;*.json)|*.txt;*.md;*.json|TXT và Markdown (*.txt;*.md)|*.txt;*.md|JSON (*.json)|*.json",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
            await ImportFilesAsync(dialog.FileNames);
    }

    private void PromptList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggedPrompt = null;
        // Giữ thao tác bấm ngôi sao và thanh cuộn như bình thường.
        for (var element = e.OriginalSource as DependencyObject; element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or ScrollBar) return;
            if (element is ListBoxItem { DataContext: PromptItem item })
            {
                _dragStart = e.GetPosition(PromptListBox);
                _draggedPrompt = item;
                return;
            }
            if (element == PromptListBox) return;
        }
    }

    private void PromptList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _viewModel.IsTrashView || !_viewModel.CanImportTextFiles)
        {
            _draggedPrompt = null;
            return;
        }
        if (_draggedPrompt is null) return;
        var position = e.GetPosition(PromptListBox);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var source = _draggedPrompt;
        _draggedPrompt = null;
        try
        {
            DragDrop.DoDragDrop(PromptListBox, new DataObject(PromptDragFormat, source), DragDropEffects.Move);
        }
        finally { ClearInsertionMarker(); }
    }

    private ListBoxItem? GetDropTarget(DragEventArgs e)
    {
        var hit = PromptListBox.InputHitTest(e.GetPosition(PromptListBox)) as DependencyObject;
        return hit is null ? null : ItemsControl.ContainerFromElement(PromptListBox, hit) as ListBoxItem;
    }

    private void PromptList_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PromptDragFormat)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ClearInsertionMarker();
        if (_viewModel.IsTrashView || !_viewModel.CanImportTextFiles) return;

        var target = GetDropTarget(e);
        if (target?.DataContext is not PromptItem item ||
            e.Data.GetData(PromptDragFormat) is not PromptItem source || item.Id == source.Id) return;

        e.Effects = DragDropEffects.Move;
        var after = e.GetPosition(target).Y >= target.ActualHeight / 2;
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is not null)
        {
            _insertionAdorner = new InsertionAdorner(target, after,
                (Brush)FindResource("AccentBrush"));
            layer.Add(_insertionAdorner);
        }

        // Cuộn từng mục khi kéo sát mép danh sách dài.
        var y = e.GetPosition(PromptListBox).Y;
        var index = PromptListBox.Items.IndexOf(item);
        if (y < 28 && index > 0) PromptListBox.ScrollIntoView(PromptListBox.Items[index - 1]);
        else if (y > PromptListBox.ActualHeight - 28 && index < PromptListBox.Items.Count - 1)
            PromptListBox.ScrollIntoView(PromptListBox.Items[index + 1]);
    }

    private void PromptList_PreviewDragLeave(object sender, DragEventArgs e) => ClearInsertionMarker();

    private void PromptList_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PromptDragFormat)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ClearInsertionMarker();
        if (_viewModel.IsTrashView || !_viewModel.CanImportTextFiles) return;
        if (GetDropTarget(e) is ListBoxItem { DataContext: PromptItem target } container &&
            e.Data.GetData(PromptDragFormat) is PromptItem source && source.Id != target.Id)
        {
            _viewModel.MovePrompt(source, target, e.GetPosition(container).Y >= container.ActualHeight / 2);
            e.Effects = DragDropEffects.Move;
        }
    }

    private void ClearInsertionMarker()
    {
        if (_insertionAdorner is null) return;
        AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement)?.Remove(_insertionAdorner);
        _insertionAdorner = null;
    }

    private sealed class InsertionAdorner(UIElement element, bool after, Brush brush) : Adorner(element)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            IsHitTestVisible = false;
            var y = after ? AdornedElement.RenderSize.Height : 0;
            drawingContext.DrawLine(new Pen(brush, 3), new Point(0, y),
                new Point(AdornedElement.RenderSize.Width, y));
        }
    }

    private static bool IsSupportedImportFile(string path) => TextFileImportService.IsSupportedFile(path) ||
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var canImport = _viewModel.CanImportTextFiles &&
                        (e.AllowedEffects & DragDropEffects.Copy) != 0 &&
                        paths is not null && paths.Any(path => IsSupportedImportFile(path) && File.Exists(path));
        e.Effects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        FileDropOverlay.Visibility = canImport ? Visibility.Visible : Visibility.Collapsed;
        // Xử lý từ cửa sổ trước TextBox để file luôn tạo prompt mới.
        e.Handled = true;
    }

    private void MainWindow_PreviewDragLeave(object sender, DragEventArgs e)
    {
        FileDropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void MainWindow_PreviewDrop(object sender, DragEventArgs e)
    {
        FileDropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (_viewModel.CanImportTextFiles &&
            (e.AllowedEffects & DragDropEffects.Copy) != 0 &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            e.Effects = DragDropEffects.Copy;
            await ImportFilesAsync(paths);
        }
    }

    private async Task ImportFilesAsync(string[] paths)
    {
        var errors = new List<string>();
        var count = 0;
        var uniquePaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var textPaths = uniquePaths.Where(path => !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)).ToArray();
        try
        {
            if (textPaths.Length > 0)
            {
                var result = await _viewModel.ImportTextFilesAsync(textPaths);
                count += result.ImportedCount;
                errors.AddRange(result.Errors.Select(error => $"• {Path.GetFileName(error.FilePath)}: {error.Message}"));
            }
        }
        catch (Exception ex)
        {
            errors.Add($"• TXT/MD: {ex.Message}");
        }
        foreach (var path in uniquePaths.Except(textPaths, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                count += _viewModel.ImportJson(path);
            }
            catch (Exception ex)
            {
                errors.Add($"• {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        var details = errors.Count == 0 ? string.Empty : "\n\n" + string.Join("\n", errors.Take(10));
        if (errors.Count > 10) details += $"\n... và {errors.Count - 10:N0} lỗi khác.";
        MessageBox.Show(this, $"Đã nhập {count:N0} prompt." + details, "Nhập file",
            MessageBoxButton.OK, errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsImportingTextFiles)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Đang nhập file. Vui lòng chờ hoàn tất trước khi đóng ứng dụng.",
                "Prompt Notebook", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Ghi nhớ vị trí, kích thước cửa sổ và độ rộng cột
        _settings.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }
        else
        {
            _settings.WindowWidth = RestoreBounds.Width;
            _settings.WindowHeight = RestoreBounds.Height;
            _settings.WindowLeft = RestoreBounds.Left;
            _settings.WindowTop = RestoreBounds.Top;
        }

        var leftActual = LeftColumn.ActualWidth;
        var rightActual = RightColumn.ActualWidth;
        var totalActual = leftActual + rightActual;
        if (totalActual > 100)
        {
            _settings.LeftColumnWidthRatio = Math.Round((leftActual / totalActual) * 100.0, 2);
            _settings.LeftColumnPixelWidth = Math.Round(leftActual, 0);
        }

        // Lưu toàn bộ thay đổi chưa lưu + lưu cấu hình + tạo bản sao lưu tự động
        _viewModel.OnApplicationClosing(_settings);
    }
}
