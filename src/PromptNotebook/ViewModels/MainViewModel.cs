using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using PromptNotebook.Helpers;
using PromptNotebook.Models;
using PromptNotebook.Services;

namespace PromptNotebook.ViewModels;

/// <summary>
/// ViewModel trung tâm điều phối giao diện hai cột của Prompt Notebook.
/// - Quản lý danh sách prompt (chỉ chứa metadata để tối ưu bộ nhớ khi có >= 5.000 prompt).
/// - Chỉ tải nội dung đầy đủ (Content) khi người dùng chọn một prompt.
/// - Tự động lưu sau khi ngừng gõ 700ms (debounce) và lưu tức thì khi chuyển prompt hoặc đóng ứng dụng.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private readonly SettingsService _settingsService;
    private readonly JsonExportImportService _jsonService;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _searchDebounceTimer;

    private ObservableCollection<PromptItem> _prompts = [];
    private ObservableCollection<string> _filterCategories = ["Tất cả danh mục"];
    private ObservableCollection<string> _editableCategories = ["Chung", "Công việc", "Lập trình", "Dịch thuật", "Viết lách", "Hình ảnh / Video"];

    private PromptItem? _selectedPrompt;
    private string _editingTitle = string.Empty;
    private string _editingContent = string.Empty;
    private string _editingCategory = "Chung";
    private bool _editingIsFavorite;

    private string _searchKeyword = string.Empty;
    private string _selectedCategoryFilter = "Tất cả danh mục";
    private bool _onlyFavoritesFilter;
    private bool _isTrashView;

    private string _saveStatusText = "Đã lưu";
    private bool _isDirty;
    private bool _suppressEditorTracking;

    private bool _isDarkMode = true;
    private double _editorFontSize = 14.0;
    private string _statusBarSummary = "Sẵn sàng";

    public MainViewModel(
        DatabaseService dbService,
        SettingsService settingsService,
        JsonExportImportService jsonService,
        AppSettings initialSettings)
    {
        _dbService = dbService;
        _settingsService = settingsService;
        _jsonService = jsonService;

        _isDarkMode = initialSettings.IsDarkMode;
        _editorFontSize = initialSettings.EditorFontSize;

        // Bộ hẹn giờ tự động lưu sau 700ms kể từ lần gõ phím cuối cùng
        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            FlushPendingSave();
        };

        // Bộ hẹn giờ tìm kiếm tức thì (debounce nhẹ 90ms để không khóa UI khi gõ liên tục, tổng phản hồi < 200ms)
        _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RefreshPromptList(keepSelection: true);
        };

        // Khởi tạo các lệnh MVVM
        NewPromptCommand = new RelayCommand(ExecuteNewPrompt);
        SaveNowCommand = new RelayCommand(FlushPendingSave);
        CopyContentCommand = new RelayCommand(ExecuteCopyContent, () => HasSelectedPrompt);
        ToggleFavoriteCommand = new RelayCommand(ExecuteToggleFavorite);
        DuplicatePromptCommand = new RelayCommand(ExecuteDuplicatePrompt);
        DeleteOrRestorePromptCommand = new RelayCommand(ExecuteDeleteOrRestorePrompt);
        PermanentDeleteCommand = new RelayCommand(ExecutePermanentDelete);
        EmptyTrashCommand = new RelayCommand(ExecuteEmptyTrash, () => IsTrashView && Prompts.Count > 0);
        ToggleThemeCommand = new RelayCommand(() => IsDarkMode = !IsDarkMode);
        IncreaseFontSizeCommand = new RelayCommand(() => EditorFontSize = Math.Min(32.0, EditorFontSize + 1.0));
        DecreaseFontSizeCommand = new RelayCommand(() => EditorFontSize = Math.Max(10.0, EditorFontSize - 1.0));
        ClearSelectionOrSearchCommand = new RelayCommand(ExecuteEscapeKey);
        BackupNowCommand = new RelayCommand(ExecuteManualBackup);
    }

    // ==================== THUỘC TÍNH BINDING ====================

    public ObservableCollection<PromptItem> Prompts
    {
        get => _prompts;
        private set => SetProperty(ref _prompts, value);
    }

    public ObservableCollection<string> FilterCategories
    {
        get => _filterCategories;
        private set => SetProperty(ref _filterCategories, value);
    }

    public ObservableCollection<string> EditableCategories
    {
        get => _editableCategories;
        private set => SetProperty(ref _editableCategories, value);
    }

    public PromptItem? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            if (ReferenceEquals(_selectedPrompt, value))
                return;

            // Lưu ngay lập tức mọi thay đổi chưa lưu của prompt trước đó trước khi chuyển sang prompt mới
            FlushPendingSave();

            if (SetProperty(ref _selectedPrompt, value))
            {
                OnPropertyChanged(nameof(HasSelectedPrompt));
                OnPropertyChanged(nameof(IsEditorEnabled));
                LoadSelectedPromptIntoEditor();
            }
        }
    }

    public bool HasSelectedPrompt => _selectedPrompt is not null;

    public bool IsEditorEnabled => _selectedPrompt is not null && !_isTrashView;

    public string EditingTitle
    {
        get => _editingTitle;
        set
        {
            if (SetProperty(ref _editingTitle, value) && !_suppressEditorTracking && _selectedPrompt is not null)
            {
                _selectedPrompt.Title = string.IsNullOrWhiteSpace(value) ? "Prompt không tên" : value;
                ScheduleAutoSave();
            }
        }
    }

    public string EditingContent
    {
        get => _editingContent;
        set
        {
            if (SetProperty(ref _editingContent, value) && !_suppressEditorTracking && _selectedPrompt is not null)
            {
                OnPropertyChanged(nameof(ContentLengthInfo));
                ScheduleAutoSave();
            }
        }
    }

    public string EditingCategory
    {
        get => _editingCategory;
        set
        {
            if (SetProperty(ref _editingCategory, value) && !_suppressEditorTracking && _selectedPrompt is not null)
            {
                _selectedPrompt.Category = string.IsNullOrWhiteSpace(value) ? "Chung" : value.Trim();
                ScheduleAutoSave();
            }
        }
    }

    public bool EditingIsFavorite
    {
        get => _editingIsFavorite;
        set
        {
            if (SetProperty(ref _editingIsFavorite, value) && !_suppressEditorTracking && _selectedPrompt is not null)
            {
                _selectedPrompt.IsFavorite = value;
                OnPropertyChanged(nameof(EditingFavoriteText));
                ScheduleAutoSave();
            }
        }
    }

    public string EditingFavoriteText => EditingIsFavorite ? "★ Đã yêu thích" : "☆ Yêu thích";

    public string ContentLengthInfo => $"{_editingContent.Length:N0} ký tự";

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        }
    }

    public string SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedCategoryFilter, value))
            {
                RefreshPromptList(keepSelection: true);
            }
        }
    }

    public bool OnlyFavoritesFilter
    {
        get => _onlyFavoritesFilter;
        set
        {
            if (SetProperty(ref _onlyFavoritesFilter, value))
            {
                if (value && _isTrashView)
                {
                    _isTrashView = false;
                    OnPropertyChanged(nameof(IsTrashView));
                    OnPropertyChanged(nameof(TrashToggleText));
                }
                RefreshPromptList(keepSelection: true);
            }
        }
    }

    public bool IsTrashView
    {
        get => _isTrashView;
        set
        {
            if (SetProperty(ref _isTrashView, value))
            {
                OnPropertyChanged(nameof(TrashToggleText));
                OnPropertyChanged(nameof(DeleteButtonText));
                OnPropertyChanged(nameof(IsEditorEnabled));
                RefreshPromptList(keepSelection: false);
            }
        }
    }

    public string TrashToggleText => IsTrashView ? "← Quay lại danh sách" : "Thùng rác";

    public string DeleteButtonText => IsTrashView ? "Khôi phục" : "Xóa";

    public string SaveStatusText
    {
        get => _saveStatusText;
        private set => SetProperty(ref _saveStatusText, value);
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                OnPropertyChanged(nameof(ThemeButtonText));
                ThemeChanged?.Invoke(this, value);
            }
        }
    }

    public string ThemeButtonText => IsDarkMode ? "☀ Sáng" : "🌙 Tối";

    public double EditorFontSize
    {
        get => _editorFontSize;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 10.0, 32.0);
            if (SetProperty(ref _editorFontSize, clamped))
            {
                OnPropertyChanged(nameof(FontSizeDisplay));
            }
        }
    }

    public string FontSizeDisplay => $"{EditorFontSize:0}px";

    public string StatusBarSummary
    {
        get => _statusBarSummary;
        private set => SetProperty(ref _statusBarSummary, value);
    }

    public event EventHandler<bool>? ThemeChanged;
    public event EventHandler? FocusTitleRequested;
    public event EventHandler? FocusSearchRequested;

    // ==================== COMMANDS ====================

    public RelayCommand NewPromptCommand { get; }
    public RelayCommand SaveNowCommand { get; }
    public RelayCommand CopyContentCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand DuplicatePromptCommand { get; }
    public RelayCommand DeleteOrRestorePromptCommand { get; }
    public RelayCommand PermanentDeleteCommand { get; }
    public RelayCommand EmptyTrashCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand IncreaseFontSizeCommand { get; }
    public RelayCommand DecreaseFontSizeCommand { get; }
    public RelayCommand ClearSelectionOrSearchCommand { get; }
    public RelayCommand BackupNowCommand { get; }

    // ==================== LOGIC NGHIỆP VỤ ====================

    public void Initialize(long? lastSelectedPromptId)
    {
        ReloadCategories();
        RefreshPromptList(keepSelection: false, preferredPromptId: lastSelectedPromptId);
    }

    public void ReloadCategories()
    {
        var cats = _dbService.GetCategories();
        EditableCategories = new ObservableCollection<string>(cats);

        var filterList = new List<string>(cats.Count + 1) { "Tất cả danh mục" };
        filterList.AddRange(cats);

        var currentFilter = _selectedCategoryFilter;
        FilterCategories = new ObservableCollection<string>(filterList);
        if (filterList.Contains(currentFilter))
        {
            _selectedCategoryFilter = currentFilter;
        }
        else
        {
            _selectedCategoryFilter = "Tất cả danh mục";
        }
        OnPropertyChanged(nameof(SelectedCategoryFilter));
    }

    public void RefreshPromptList(bool keepSelection = true, long? preferredPromptId = null)
    {
        FlushPendingSave();

        var targetId = preferredPromptId ?? (keepSelection ? _selectedPrompt?.Id : null);
        var items = _dbService.QueryPromptList(
            _searchKeyword,
            _selectedCategoryFilter,
            _onlyFavoritesFilter,
            _isTrashView);

        Prompts = new ObservableCollection<PromptItem>(items);

        if (targetId.HasValue)
        {
            SelectedPrompt = Prompts.FirstOrDefault(p => p.Id == targetId.Value) ?? Prompts.FirstOrDefault();
        }
        else
        {
            SelectedPrompt = Prompts.FirstOrDefault();
        }

        UpdateSummaryText();
    }

    private void LoadSelectedPromptIntoEditor()
    {
        _suppressEditorTracking = true;
        try
        {
            if (_selectedPrompt is null)
            {
                EditingTitle = string.Empty;
                EditingContent = string.Empty;
                EditingCategory = "Chung";
                EditingIsFavorite = false;
                SaveStatusText = "Đã lưu";
            }
            else
            {
                EditingTitle = _selectedPrompt.Title;
                EditingCategory = _selectedPrompt.Category;
                EditingIsFavorite = _selectedPrompt.IsFavorite;
                // Chỉ tải nội dung đầy đủ từ SQLite của đúng prompt đang chọn
                EditingContent = _dbService.GetPromptContent(_selectedPrompt.Id);
                SaveStatusText = "Đã lưu";
            }
            OnPropertyChanged(nameof(EditingFavoriteText));
            OnPropertyChanged(nameof(ContentLengthInfo));
        }
        finally
        {
            _suppressEditorTracking = false;
        }
    }

    private void ScheduleAutoSave()
    {
        if (_selectedPrompt is null || _isTrashView)
            return;

        _isDirty = true;
        SaveStatusText = "Đang lưu...";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// Lưu tức thì các thay đổi đang chờ vào SQLite.
    /// Được gọi khi bộ hẹn giờ 700ms kết thúc, khi người dùng bấm Ctrl+S, khi chuyển sang prompt khác, hoặc khi đóng ứng dụng.
    /// </summary>
    public void FlushPendingSave()
    {
        _autoSaveTimer.Stop();
        if (!_isDirty || _selectedPrompt is null)
            return;

        var titleToSave = string.IsNullOrWhiteSpace(_editingTitle) ? "Prompt không tên" : _editingTitle.Trim();
        var categoryToSave = string.IsNullOrWhiteSpace(_editingCategory) ? "Chung" : _editingCategory.Trim();

        var updatedAt = _dbService.UpdatePrompt(
            _selectedPrompt.Id,
            titleToSave,
            _editingContent,
            categoryToSave,
            _editingIsFavorite);

        _selectedPrompt.Title = titleToSave;
        _selectedPrompt.Category = categoryToSave;
        _selectedPrompt.IsFavorite = _editingIsFavorite;
        _selectedPrompt.UpdatedAt = updatedAt;

        if (!EditableCategories.Contains(categoryToSave))
        {
            ReloadCategories();
        }

        _isDirty = false;
        SaveStatusText = "Đã lưu";
    }

    public void ExecuteNewPrompt()
    {
        FlushPendingSave();

        if (IsTrashView)
        {
            IsTrashView = false;
        }

        var defaultCategory = (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) &&
                               SelectedCategoryFilter != "Tất cả danh mục")
            ? SelectedCategoryFilter
            : "Chung";

        var created = _dbService.CreatePrompt("Prompt mới", string.Empty, defaultCategory, isFavorite: false);
        Prompts.Insert(0, created);
        SelectedPrompt = created;
        UpdateSummaryText();
        FocusTitleRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFocusSearch()
    {
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ExecuteCopyContent()
    {
        if (_selectedPrompt is null)
            return;

        FlushPendingSave();
        var textToCopy = _editingContent ?? string.Empty;

        try
        {
            Clipboard.SetDataObject(textToCopy, true);
            SaveStatusText = "Đã sao chép!";
            StatusBarSummary = $"Đã sao chép {textToCopy.Length:N0} ký tự vào Clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể truy cập Clipboard: {ex.Message}",
                "Prompt Notebook",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void ExecuteToggleFavorite(object? parameter)
    {
        var target = parameter as PromptItem ?? _selectedPrompt;
        if (target is null)
            return;

        if (ReferenceEquals(target, _selectedPrompt))
        {
            EditingIsFavorite = !EditingIsFavorite;
            FlushPendingSave();
        }
        else
        {
            target.IsFavorite = !target.IsFavorite;
            target.UpdatedAt = _dbService.UpdateMetadataOnly(target.Id, target.Title, target.Category, target.IsFavorite);
        }
    }

    public void ExecuteDuplicatePrompt(object? parameter)
    {
        FlushPendingSave();
        var target = parameter as PromptItem ?? _selectedPrompt;
        if (target is null)
            return;

        var copy = _dbService.DuplicatePrompt(target.Id);
        if (copy is not null)
        {
            Prompts.Insert(0, copy);
            SelectedPrompt = copy;
            UpdateSummaryText();
        }
    }

    public void ExecuteDeleteOrRestorePrompt(object? parameter)
    {
        FlushPendingSave();
        var target = parameter as PromptItem ?? _selectedPrompt;
        if (target is null)
            return;

        if (IsTrashView)
        {
            _dbService.RestoreFromTrash(target.Id);
            Prompts.Remove(target);
            SelectedPrompt = Prompts.FirstOrDefault();
            StatusBarSummary = $"Đã khôi phục \"{target.Title}\".";
            UpdateSummaryText();
            return;
        }

        var confirm = MessageBox.Show(
            $"Chuyển prompt \"{target.Title}\" vào thùng rác?\n(Bạn có thể khôi phục lại bất cứ lúc nào từ Thùng rác)",
            "Xác nhận xóa prompt",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        var index = Prompts.IndexOf(target);
        _dbService.MoveToTrash(target.Id);
        Prompts.Remove(target);

        if (Prompts.Count > 0)
        {
            var nextIndex = Math.Clamp(index, 0, Prompts.Count - 1);
            SelectedPrompt = Prompts[nextIndex];
        }
        else
        {
            SelectedPrompt = null;
        }

        UpdateSummaryText();
    }

    public void ExecutePermanentDelete(object? parameter)
    {
        var target = parameter as PromptItem ?? _selectedPrompt;
        if (target is null)
            return;

        var confirm = MessageBox.Show(
            $"Xóa vĩnh viễn prompt \"{target.Title}\"?\nHành động này không thể hoàn tác.",
            "Xóa vĩnh viễn",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        _dbService.PermanentlyDelete(target.Id);
        Prompts.Remove(target);
        SelectedPrompt = Prompts.FirstOrDefault();
        UpdateSummaryText();
    }

    public void ExecuteEmptyTrash()
    {
        if (!IsTrashView || Prompts.Count == 0)
            return;

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa vĩnh viễn toàn bộ {Prompts.Count:N0} prompt trong thùng rác?",
            "Dọn sạch thùng rác",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        _dbService.EmptyTrash();
        RefreshPromptList(keepSelection: false);
    }

    public void ExecuteEscapeKey()
    {
        if (!string.IsNullOrEmpty(SearchKeyword))
        {
            SearchKeyword = string.Empty;
            return;
        }

        SelectedPrompt = null;
    }

    public void ExecuteManualBackup()
    {
        FlushPendingSave();
        var path = _dbService.PerformSafeBackup();
        StatusBarSummary = $"Đã sao lưu cơ sở dữ liệu an toàn: {System.IO.Path.GetFileName(path)} (giữ tối đa 7 bản).";
    }

    public int ExportJson(string filePath)
    {
        FlushPendingSave();
        var count = _jsonService.ExportToJsonFile(_dbService, filePath, includeDeleted: false);
        StatusBarSummary = $"Đã xuất {count:N0} prompt ra file JSON UTF-8.";
        return count;
    }

    public int ImportJson(string filePath)
    {
        FlushPendingSave();
        var count = _jsonService.ImportFromJsonFile(_dbService, filePath);
        ReloadCategories();
        RefreshPromptList(keepSelection: true);
        StatusBarSummary = $"Đã nhập thành công {count:N0} prompt từ file JSON UTF-8.";
        return count;
    }

    public void OnApplicationClosing(AppSettings settings)
    {
        // Đảm bảo lưu toàn bộ thay đổi ngay lập tức trước khi thoát
        FlushPendingSave();

        settings.IsDarkMode = IsDarkMode;
        settings.EditorFontSize = EditorFontSize;
        settings.LastSelectedPromptId = SelectedPrompt?.Id;
        _settingsService.Save(settings);

        // Thực hiện sao lưu tự động an toàn (giữ 7 bản gần nhất)
        try
        {
            _dbService.PerformSafeBackup(maxBackupsToKeep: 7);
        }
        catch
        {
            // Không chặn đóng ứng dụng nếu ổ đĩa đích bị khóa
        }
    }

    private void UpdateSummaryText()
    {
        var modeLabel = IsTrashView ? "trong thùng rác" : "prompt";
        StatusBarSummary = $"Hiển thị {Prompts.Count:N0} {modeLabel}";
    }
}
