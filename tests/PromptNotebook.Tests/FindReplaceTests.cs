using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;
using PromptNotebook.Views;

namespace PromptNotebook.Tests;

internal static class FindReplaceTests
{
    // Chỉ tạo các control trong bộ nhớ; không mở cửa sổ hay gửi phím/chuột vào máy.
    public static void Run(string testRoot)
    {
        var application = Application.Current ?? new Application();
        application.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
        application.Resources[typeof(Button)] = new Style(typeof(Button));
        application.Resources[typeof(TextBox)] = new Style(typeof(TextBox));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PromptNotebook;component/Themes/DarkTheme.xaml")
        });

        var db = new DatabaseService(Path.Combine(testRoot, "find-replace.db"));
        db.InitializeDatabase();
        const string original = "Mèo mèo MÈO\r\n中文 mèo";
        var prompt = db.CreatePrompt("Tìm thay", original);
        var other = db.CreatePrompt("Không thay", "mèo");
        var settings = new AppSettings { EditorViewMode = "Preview" };
        var settingsService = new SettingsService(Path.Combine(testRoot, "find-replace-settings.json"));
        var vm = new MainViewModel(db, settingsService, new JsonExportImportService(), settings);
        var window = new MainWindow(vm, settingsService, settings);
        vm.Initialize(prompt.Id);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        var editor = (TextBox)window.FindName("ContentEditorTextBox");
        var query = (TextBox)window.FindName("FindTextBox");
        var replacement = (TextBox)window.FindName("ReplaceTextBox");
        var status = (TextBlock)window.FindName("FindStatusText");
        Invoke(window, "OpenFindBar", true);
        window.Measure(new Size(1180, 740));
        window.Arrange(new Rect(0, 0, 1180, 740));
        window.UpdateLayout();
        editor.ApplyTemplate();
        Check(vm.IsSplitMode, "Tìm kiếm hiện khung soạn thảo khi đang xem trước.");
        query.Text = "mèo";
        Check(editor.SelectionStart == 0 && editor.SelectionLength == 3 && status.Text.StartsWith("4 "),
            $"Tìm không phân biệt hoa/thường, đếm kết quả và chọn đúng chữ tiếng Việt. (vị trí={editor.SelectionStart}, dài={editor.SelectionLength}, trạng thái={status.Text}, độ dài nội dung={editor.Text.Length})");
        Invoke(window, "FindMatch", true, false);
        Check(editor.SelectionStart == original.LastIndexOf("mèo", StringComparison.OrdinalIgnoreCase),
            "Tìm trước quay vòng về cuối nội dung.");
        Invoke(window, "FindMatch", false, false);
        Check(editor.SelectionStart == 0, "Tìm tiếp quay vòng về đầu nội dung.");

        replacement.Text = "mèo mới";
        Invoke(window, "ReplaceCurrentMatch");
        Check(editor.Text.StartsWith("mèo mới mèo") && editor.SelectionStart == 8,
            "Thay từng chỗ đi tiếp sau chữ vừa chèn, kể cả chữ thay thế có chứa từ đang tìm.");
        editor.Undo();
        Check(editor.Text == original, $"Ctrl+Z hoàn tác thay từng chỗ. Thực tế: {System.Text.Json.JsonSerializer.Serialize(editor.Text)}, còn hoàn tác: {editor.CanUndo}");

        replacement.Text = "猫";
        Invoke(window, "ReplaceAllMatches");
        Check(editor.Text == "猫 猫 猫\r\n中文 猫", "Thay tất cả giữ nguyên xuống dòng và chữ Unicode khác.");
        vm.FlushPendingSave();
        Check(db.GetPromptContent(prompt.Id) == editor.Text && db.GetPromptContent(other.Id) == "mèo",
            "Lưu đúng prompt đang mở, không thay prompt khác.");
        editor.Undo();
        vm.FlushPendingSave();
        Check(db.GetPromptContent(prompt.Id) == original, "Một lần Ctrl+Z khôi phục toàn bộ thay tất cả và lưu lại.");

        query.Text = "mèo";
        replacement.Text = string.Empty;
        Invoke(window, "ReplaceAllMatches");
        Check(editor.Text == "  \r\n中文 ", "Cho phép thay bằng chữ rỗng để xóa các kết quả.");
        editor.Undo();
        query.Text = string.Empty;
        Invoke(window, "ReplaceAllMatches");
        Check(editor.Text == original && status.Text.Contains("Không phân biệt"), "Từ tìm rỗng không thay nội dung.");
        query.Text = "không tồn tại";
        Invoke(window, "ReplaceCurrentMatch");
        Check(editor.Text == original && status.Text.Contains("Không tìm thấy"), "Không có kết quả thì giữ nguyên nội dung.");

        query.Text = "mèo";
        vm.IsTrashView = true;
        var trash = db.CreatePrompt("Trong thùng rác", original);
        db.MoveToTrash(trash.Id);
        vm.RefreshPromptList(preferredPromptId: trash.Id);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Invoke(window, "ReplaceAllMatches");
        Check(db.GetPromptContent(trash.Id) == original && editor.Text == original,
            "Không thay nội dung khi đang xem thùng rác.");
        Console.WriteLine("[PASS] Tìm/thay: quay vòng, Unicode, lưu, phạm vi một prompt, hoàn tác, từ rỗng và thùng rác.");
    }

    private static object? Invoke(MainWindow window, string method, params object[] arguments) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, arguments);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[FAILED] " + message);
    }
}
