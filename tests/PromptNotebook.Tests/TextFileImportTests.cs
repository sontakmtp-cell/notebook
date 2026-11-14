using System.IO;
using System.Text;
using System.Windows.Threading;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;

namespace PromptNotebook.Tests;

internal static class TextFileImportTests
{
    public static void Run(string testRoot)
    {
        var folder = Path.Combine(testRoot, "text-files");
        Directory.CreateDirectory(folder);
        const string text = "Tiếng Việt có dấu — 中文 — 😀\r\n\r\nDòng cuối\n";
        const string markdown = "# Tiêu đề\n\n**Đậm** và `code`\n- Một\n- Hai\n";
        var txtPath = Path.Combine(folder, "Nội dung.TXT");
        var mdPath = Path.Combine(folder, "Markdown.Md");
        var emptyPath = Path.Combine(folder, "Trống.txt");
        var utf16Path = Path.Combine(folder, "Unicode.txt");
        var utf32Path = Path.Combine(folder, "Unicode32.md");
        var invalidPath = Path.Combine(folder, "Sai bảng mã.txt");
        var lockedPath = Path.Combine(folder, "Đang khóa.txt");
        var missingPath = Path.Combine(folder, "Không tồn tại.md");
        var unsupportedPath = Path.Combine(folder, "Không hỗ trợ.json");
        var directoryPath = Path.Combine(folder, "Thư mục.txt");
        File.WriteAllText(txtPath, text, new UTF8Encoding(false));
        File.WriteAllText(mdPath, markdown, new UTF8Encoding(true));
        File.WriteAllText(emptyPath, string.Empty);
        File.WriteAllText(utf16Path, text, Encoding.Unicode);
        File.WriteAllText(utf32Path, text, Encoding.UTF32);
        File.WriteAllBytes(invalidPath, [0xC3, 0x28]);
        File.WriteAllText(lockedPath, text);
        File.WriteAllText(unsupportedPath, "{}");
        Directory.CreateDirectory(directoryPath);

        var paths = new[] { txtPath, mdPath, emptyPath, utf16Path, utf32Path, txtPath,
            invalidPath, lockedPath, missingPath, unsupportedPath, directoryPath };
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var (prompts, errors) = new TextFileImportService().ReadFilesAsync(paths, "Tài liệu")
                .GetAwaiter().GetResult();
            Check(prompts.Count == 5 && errors.Count == 5, "Nhập nhiều file: bỏ qua file trùng đường dẫn, báo riêng file lỗi.");
            Check(prompts[0].Title == "Nội dung" && prompts[0].Content == text,
                "Tên file thành tiêu đề; giữ nguyên tiếng Việt, Trung, emoji và xuống dòng CRLF/LF.");
            Check(prompts[1].Title == "Markdown" && prompts[1].Content == markdown,
                "Markdown UTF-8 BOM giữ nguyên toàn bộ cú pháp và không thêm BOM vào nội dung.");
            Check(prompts[2].Content == string.Empty && prompts[3].Content == text && prompts[4].Content == text,
                "Hỗ trợ file trống và UTF-16/UTF-32 BOM.");
            Check(prompts.All(p => p.Category == "Tài liệu" && !p.IsDeleted && !p.IsFavorite),
                "Các file tạo prompt mới trong đúng danh mục.");
        }

        // Kiểm tra cả lưu SQLite và trạng thái giao diện khi người dùng đang sửa/lọc/xem thùng rác.
        var db = new DatabaseService(Path.Combine(testRoot, "text-import.db"));
        db.InitializeDatabase();
        var original = db.CreatePrompt("Đang sửa", "Nội dung cũ", "Tài liệu", true);
        var viewModel = new MainViewModel(db,
            new SettingsService(Path.Combine(testRoot, "text-import-settings.json")),
            new JsonExportImportService(), new AppSettings());
        viewModel.Initialize(original.Id);
        viewModel.EditingContent = "Thay đổi chưa tự lưu";
        viewModel.SelectedCategoryFilter = "Tài liệu";
        viewModel.OnlyFavoritesFilter = true;
        viewModel.SearchKeyword = "không khớp file mới";
        viewModel.IsTrashView = true;

        var result = RunOnDispatcher(() => viewModel.ImportTextFilesAsync([txtPath, mdPath, missingPath]));
        Check(result.ImportedCount == 2 && result.Errors.Count == 1, "File đọc được vẫn lưu khi có file lỗi.");
        Check(db.GetPromptContent(original.Id) == "Thay đổi chưa tự lưu", "Không mất chỉnh sửa chưa tự lưu khi nhập file.");
        Check(!viewModel.IsTrashView && !viewModel.OnlyFavoritesFilter && viewModel.SearchKeyword == string.Empty &&
              viewModel.SelectedCategoryFilter == "Tất cả danh mục", "Bỏ bộ lọc để thấy prompt mới sau khi nhập.");
        Check(viewModel.EditingTitle == "Markdown" && viewModel.EditingContent == markdown &&
              viewModel.EditingCategory == "Tài liệu", "Tự chọn prompt vừa nhập và tải đúng nội dung từ SQLite.");
        Check(viewModel.CanImportTextFiles && !viewModel.IsImportingTextFiles, "Cho phép nhập tiếp sau khi hoàn tất.");

        var repeated = RunOnDispatcher(() => viewModel.ImportTextFilesAsync([txtPath]));
        Check(repeated.ImportedCount == 1 && db.QueryPromptList().Count(p => p.Title == "Nội dung") == 2,
            "Nhập lại file tạo prompt riêng, không ghi đè nội dung đã lưu.");
        var selectedId = viewModel.SelectedPrompt!.Id;
        var failed = RunOnDispatcher(() => viewModel.ImportTextFilesAsync([missingPath, unsupportedPath]));
        Check(failed.ImportedCount == 0 && failed.Errors.Count == 2 && viewModel.SelectedPrompt.Id == selectedId &&
              viewModel.CanImportTextFiles, "Lô toàn file lỗi giữ nguyên prompt đang chọn và cho phép thử lại.");

        Console.WriteLine("[PASS] Nhập TXT/MD: Unicode, Markdown, nhiều file, file lỗi, SQLite, giữ chỉnh sửa và hiển thị prompt mới.");
    }

    private static T RunOnDispatcher<T>(Func<Task<T>> action)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = action();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                    TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"[FAILED] {message}");
    }
}
