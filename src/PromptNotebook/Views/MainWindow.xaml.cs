using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;

namespace PromptNotebook.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly DatabaseService _dbService;

    public MainWindow(MainViewModel viewModel, SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _settingsService = settingsService;
        _settings = settings;
        _dbService = new DatabaseService();

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
            var newTitle = dialog.PromptTitle;
            var newCategory = string.IsNullOrWhiteSpace(dialog.PromptCategory) ? "Chung" : dialog.PromptCategory;

            item.Title = newTitle;
            item.Category = newCategory;
            item.UpdatedAt = _dbService.UpdateMetadataOnly(item.Id, newTitle, newCategory, item.IsFavorite);

            if (ReferenceEquals(item, _viewModel.SelectedPrompt))
            {
                _viewModel.EditingTitle = newTitle;
                _viewModel.EditingCategory = newCategory;
                _viewModel.FlushPendingSave();
            }

            _viewModel.ReloadCategories();
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

    private void ImportJsonButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Nhập dữ liệu Prompt từ file JSON UTF-8",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var count = _viewModel.ImportJson(dialog.FileName);
                MessageBox.Show(
                    this,
                    $"Đã nhập thành công {count:N0} prompt vào cơ sở dữ liệu.",
                    "Nhập JSON thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Lỗi khi nhập file JSON:\n{ex.Message}",
                    "Lỗi nhập dữ liệu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
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
