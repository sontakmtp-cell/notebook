using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using PromptNotebook.Models;
using PromptNotebook.Services;

namespace PromptNotebook.Views;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<EditorTab> _tabs = [];
    private EditorTab? _active;
    private int _untitledCount;

    public MainWindow(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        InitializeComponent();
        Width = Math.Clamp(settings.WindowWidth, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(settings.WindowHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        if (settings.IsMaximized)
            WindowState = WindowState.Maximized;

        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        Editor.TextChanged += (_, _) => UpdateStatus();
        Tabs.ItemsSource = _tabs;
        ApplyEditorSettings();
        AddTab(new EditorTab($"Không tên {++_untitledCount}"));
    }

    private void AddTab(EditorTab tab)
    {
        tab.PropertyChanged += TabPropertyChanged;
        _tabs.Add(tab);
        Tabs.SelectedItem = tab;
        Tabs.ScrollIntoView(tab);
        Editor.Focus();
    }

    private void TabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _active))
            UpdateStatus();
    }

    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is not EditorTab tab || ReferenceEquals(tab, _active))
            return;

        if (_active is { } previous)
        {
            previous.Caret = Editor.CaretOffset;
            var selection = Editor.TextArea.Selection;
            previous.SelectionAnchor = selection.IsEmpty ? previous.Caret :
                Editor.Document.GetOffset(selection.StartPosition.Location);
            previous.SelectionEnd = selection.IsEmpty ? previous.Caret :
                Editor.Document.GetOffset(selection.EndPosition.Location);
            previous.ScrollX = Editor.HorizontalOffset;
            previous.ScrollY = Editor.VerticalOffset;
        }

        _active = tab;
        Editor.Document = tab.Document;
        Editor.CaretOffset = Math.Min(tab.Caret, tab.Document.TextLength);
        var anchor = Math.Clamp(tab.SelectionAnchor, 0, tab.Document.TextLength);
        var end = Math.Clamp(tab.SelectionEnd, 0, tab.Document.TextLength);
        Editor.TextArea.Selection = Selection.Create(Editor.TextArea, anchor, end);
        Editor.UpdateLayout();
        Editor.ScrollToHorizontalOffset(tab.ScrollX);
        Editor.ScrollToVerticalOffset(tab.ScrollY);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_active is not { } tab)
            return;

        var caret = Editor.TextArea.Caret;
        PositionStatus.Text = $"Dòng {caret.Line}, Cột {caret.Column}";
        CountStatus.Text = $"{tab.Document.LineCount:N0} dòng · {tab.Document.TextLength:N0} ký tự";
        EncodingStatus.Text = "UTF-8";
        KindStatus.Text = "TXT";
        SaveStatus.Text = tab.IsDirty ? "Chưa lưu" : "Đã lưu";
        UndoButton.IsEnabled = Editor.CanUndo;
        RedoButton.IsEnabled = Editor.CanRedo;
        Title = $"{tab.Name}{(tab.IsDirty ? " •" : "")} — Notebook";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N:
                    NewClick(this, e);
                    break;
                case Key.W:
                    if (_active is { } tab)
                        CloseTab(tab);
                    break;
                case Key.Tab:
                    SelectNextTab(1);
                    break;
                default:
                    base.OnPreviewKeyDown(e);
                    return;
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Tab)
        {
            SelectNextTab(-1);
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void SelectNextTab(int direction)
    {
        if (_tabs.Count == 0)
            return;
        var tab = _tabs[(Tabs.SelectedIndex + direction + _tabs.Count) % _tabs.Count];
        Tabs.SelectedItem = tab;
        Tabs.ScrollIntoView(tab);
        Editor.Focus();
    }

    private void TabsMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (VisualTreeHelper.GetChildrenCount(Tabs) == 0 ||
            VisualTreeHelper.GetChild(Tabs, 0) is not Border border ||
            border.Child is not ScrollViewer scroll)
            return;

        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void NewClick(object sender, RoutedEventArgs e) =>
        AddTab(new EditorTab($"Không tên {++_untitledCount}"));

    private void CloseTabClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is EditorTab tab)
            CloseTab(tab);
    }

    private void CloseTab(EditorTab tab)
    {
        if (!CanDiscard(tab))
            return;

        var index = _tabs.IndexOf(tab);
        if (ReferenceEquals(_active, tab) && _tabs.Count > 1)
            Tabs.SelectedItem = _tabs[index + (index == _tabs.Count - 1 ? -1 : 1)];

        tab.PropertyChanged -= TabPropertyChanged;
        _tabs.Remove(tab);
        if (_tabs.Count == 0)
            NewClick(this, new RoutedEventArgs());
        Editor.Focus();
    }

    private bool CanDiscard(EditorTab tab) => !tab.IsDirty ||
        MessageBox.Show(this, $"Bỏ thay đổi trong «{tab.Name}»?", "Notebook",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    private void UndoClick(object sender, RoutedEventArgs e)
    {
        Editor.Undo();
        Editor.Focus();
    }

    private void RedoClick(object sender, RoutedEventArgs e)
    {
        Editor.Redo();
        Editor.Focus();
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        _settings.IsDarkMode = DarkModeItem.IsChecked;
        App.ApplyTheme(_settings.IsDarkMode);
        ApplyEditorSettings();
    }

    private void WrapClick(object sender, RoutedEventArgs e)
    {
        _settings.WordWrap = WrapItem.IsChecked;
        ApplyEditorSettings();
    }

    private void IncreaseFontClick(object sender, RoutedEventArgs e) => ChangeFont(1);
    private void DecreaseFontClick(object sender, RoutedEventArgs e) => ChangeFont(-1);

    private void ChangeFont(int amount)
    {
        _settings.EditorFontSize = Math.Clamp(_settings.EditorFontSize + amount, 10, 36);
        ApplyEditorSettings();
    }

    private void ApplyEditorSettings()
    {
        DarkModeItem.IsChecked = _settings.IsDarkMode;
        WrapItem.IsChecked = _settings.WordWrap;
        Editor.WordWrap = _settings.WordWrap;
        Editor.FontSize = _settings.EditorFontSize;
        Editor.TextArea.Caret.CaretBrush = (Brush)FindResource("PrimaryTextBrush");
        Editor.TextArea.SelectionBrush = (Brush)FindResource("SurfaceSelectedBrush");
        Editor.TextArea.TextView.CurrentLineBackground = (Brush)FindResource("CurrentLineBrush");
        Editor.TextArea.TextView.CurrentLineBorder = null;
        ApplyTitleBarTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyTitleBarTheme();

    private void ApplyTitleBarTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;
        int dark = _settings.IsDarkMode ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_tabs.All(CanDiscard))
        {
            e.Cancel = true;
            return;
        }

        _settings.WindowWidth = RestoreBounds.Width;
        _settings.WindowHeight = RestoreBounds.Height;
        _settings.IsMaximized = WindowState == WindowState.Maximized;
        _settingsService.Save(_settings);
    }
}
