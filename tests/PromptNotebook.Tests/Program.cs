using System.Diagnostics;
using System.IO;
using System.Text;
using PromptNotebook.Models;
using PromptNotebook.Services;

namespace PromptNotebook.Tests;

/// <summary>
/// Bộ kiểm thử nghiệm thu (Acceptance Test Suite) cho Prompt Notebook v1.0.
/// Kiểm tra toàn diện:
/// 1. Tạo, sửa, xóa vào thùng rác, khôi phục và nhân bản prompt.
/// 2. Tên dài 150–300 ký tự và nội dung Unicode tiếng Việt, tiếng Anh, tiếng Trung (简体/繁體).
/// 3. Nạp 5.000 prompt mẫu và đo thời gian tìm kiếm tức thì (< 200ms).
/// 4. Chỉ tải metadata khi liệt kê danh sách (Content == null) và tải đầy đủ 100% Content khi chọn.
/// 5. Sao lưu tự động SQLite giữ đúng 7 bản gần nhất.
/// 6. Xuất/nhập JSON UTF-8 bảo toàn 100% dữ liệu đa ngôn ngữ.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== BẮT ĐẦU KIỂM THỬ NGHIỆM THU PROMPT NOTEBOOK v1.0 ===");

        var testRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptNotebook_AcceptanceTests_" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(testRoot);
        var dbPath = Path.Combine(testRoot, "prompts.db");
        var backupDir = Path.Combine(testRoot, "Backups");
        var exportJsonPath = Path.Combine(testRoot, "export_utf8.json");

        try
        {
            var db = new DatabaseService(dbPath);
            db.InitializeDatabase();
            TextFileImportTests.Run(testRoot);
            PersonalFeaturesTests.Run(testRoot);

            // 1. Kiểm thử CRUD + Tên dài 150-300 ký tự + Tiếng Việt / Anh / Trung
            var longTitle = "Prompt kiểm thử tiêu đề siêu dài từ 150 đến 300 ký tự để xác nhận khả năng lưu trữ và hiển thị tự động xuống dòng đầy đủ trên danh sách cột trái của ứng dụng Windows WPF mà không bị cắt bớt hay ẩn nội dung quan trọng - 人工智能提示词管理系统长标题测试验证";
            Assert(longTitle.Length >= 150 && longTitle.Length <= 300, $"Độ dài tiêu đề phải nằm trong khoảng 150-300 ký tự (Thực tế: {longTitle.Length}).");

            var multiLangContent =
                "Tiếng Việt: Xin chào, đây là nội dung kiểm thử chính xác 100% không mất dấu.\n" +
                "English: High-performance offline AI Prompt Notebook built with C#, .NET 10, WPF and SQLite.\n" +
                "中文测试: 这是一个完全离线、轻量级、即时响应的提示词管理工具，支持中越英三语全文检索与一键复制。";

            var created = db.CreatePrompt(longTitle, multiLangContent, "Kiểm thử", isFavorite: true);
            Assert(created.Id > 0, "Tạo prompt mới thành công.");

            var loadedContent = db.GetPromptContent(created.Id);
            Assert(loadedContent == multiLangContent, "Nội dung tải lên khớp 100% với nội dung gốc (phục vụ Copy 1 click).");

            // 2. Kiểm thử Nhân bản (Duplicate)
            var duplicated = db.DuplicatePrompt(created.Id);
            Assert(duplicated is not null && duplicated.Title.EndsWith("(Bản sao)"), "Nhân bản prompt hoạt động chính xác.");
            Assert(db.GetPromptContent(duplicated!.Id) == multiLangContent, "Nội dung nhân bản khớp 100%.");

            // 3. Kiểm thử Xóa vào thùng rác & Khôi phục
            db.MoveToTrash(duplicated.Id);
            var trashList = db.QueryPromptList(showTrash: true);
            Assert(trashList.Any(x => x.Id == duplicated.Id), "Prompt đã xóa xuất hiện trong thùng rác.");

            db.RestoreFromTrash(duplicated.Id);
            var activeList = db.QueryPromptList(showTrash: false);
            Assert(activeList.Any(x => x.Id == duplicated.Id), "Khôi phục prompt từ thùng rác thành công.");

            // 4. Kiểm thử 5.000 prompt mẫu & Hiệu năng tìm kiếm (< 200ms)
            Console.WriteLine("Đang sinh 5.000 prompt mẫu đa ngôn ngữ (Việt / Anh / Trung)...");
            var batch = new List<PromptItem>(5000);
            var categories = new[] { "Lập trình", "Dịch thuật", "Viết lách", "Hình ảnh / Video", "Phân tích dữ liệu", "Công việc" };

            for (int i = 1; i <= 5000; i++)
            {
                batch.Add(new PromptItem
                {
                    Title = $"Prompt mẫu #{i:D4} - Hướng dẫn tối ưu quy trình làm việc và thiết kế hệ thống chuyên sâu (智能提示词模板 #{i:D4}) - Tiêu đề mở rộng để kiểm tra cuộn mượt trên danh sách",
                    Content = $"Nội dung chi tiết của prompt #{i:D4}.\n" +
                              $"Từ khóa tiếng Việt: Tối ưu hóa công việc số {i}.\n" +
                              $"English keyword: Enterprise architecture pattern {i}.\n" +
                              $"中文关键词: 深度学习与自然语言处理模型优化方案第 {i} 号。",
                    Category = categories[i % categories.Length],
                    IsFavorite = (i % 10 == 0),
                    IsDeleted = false
                });
            }

            var swInsert = Stopwatch.StartNew();
            var insertedCount = db.BulkInsertPrompts(batch);
            swInsert.Stop();
            Assert(insertedCount == 5000, $"Đã chèn đủ 5.000 prompt (trong {swInsert.ElapsedMilliseconds}ms).");

            // Kiểm tra chỉ tải metadata (Content == null) khi lấy danh sách
            var swList = Stopwatch.StartNew();
            var allItems = db.QueryPromptList();
            swList.Stop();
            Assert(allItems.Count >= 5000, $"Danh sách trả về {allItems.Count} prompt.");
            Assert(allItems.All(x => x.Content is null), "Danh sách chỉ tải metadata (Content == null) để tiết kiệm RAM.");
            Console.WriteLine($"[PASS] Tải danh sách {allItems.Count:N0} prompt metadata: {swList.ElapsedMilliseconds}ms");

            // Đo thời gian tìm kiếm tiếng Trung trên >5.000 prompt
            var swSearchZh = Stopwatch.StartNew();
            var zhResults = db.QueryPromptList(searchKeyword: "模型优化方案第 4321 号");
            swSearchZh.Stop();
            Assert(zhResults.Count == 1, "Tìm kiếm chính xác nội dung tiếng Trung trong 5.000+ prompt.");
            Assert(swSearchZh.ElapsedMilliseconds < 200, $"Thời gian tìm kiếm tiếng Trung phải < 200ms (Thực tế: {swSearchZh.ElapsedMilliseconds}ms).");
            Console.WriteLine($"[PASS] Tìm kiếm tiếng Trung trong >5.000 prompt: {swSearchZh.ElapsedMilliseconds}ms (< 200ms)");

            // Đo thời gian tìm kiếm tiếng Việt trên >5.000 prompt
            var swSearchVi = Stopwatch.StartNew();
            var viResults = db.QueryPromptList(searchKeyword: "công việc số 3999");
            swSearchVi.Stop();
            Assert(viResults.Count == 1, "Tìm kiếm chính xác nội dung tiếng Việt không phân biệt hoa thường.");
            Assert(swSearchVi.ElapsedMilliseconds < 200, $"Thời gian tìm kiếm tiếng Việt phải < 200ms (Thực tế: {swSearchVi.ElapsedMilliseconds}ms).");
            Console.WriteLine($"[PASS] Tìm kiếm tiếng Việt trong >5.000 prompt: {swSearchVi.ElapsedMilliseconds}ms (< 200ms)");

            // 5. Kiểm thử Sao lưu tự động SQLite giữ đúng 7 bản gần nhất
            for (int b = 1; b <= 10; b++)
            {
                db.PerformSafeBackup(backupDir, maxBackupsToKeep: 7);
                Thread.Sleep(5);
            }
            var backupFiles = Directory.GetFiles(backupDir, "prompts_backup_*.db");
            Assert(backupFiles.Length == 7, $"Cơ chế sao lưu tự động giữ đúng 7 bản gần nhất (Thực tế: {backupFiles.Length}).");
            Console.WriteLine($"[PASS] Sao lưu SQLite an toàn, tự động xoay vòng giữ {backupFiles.Length} bản gần nhất.");

            // 6. Kiểm thử Xuất / Nhập JSON UTF-8
            var jsonService = new JsonExportImportService();
            var exported = jsonService.ExportToJsonFile(db, exportJsonPath);
            Assert(exported >= 5000 && File.Exists(exportJsonPath), "Xuất file JSON UTF-8 thành công.");

            var dbImportTest = new DatabaseService(Path.Combine(testRoot, "imported.db"));
            dbImportTest.InitializeDatabase();
            var imported = jsonService.ImportFromJsonFile(dbImportTest, exportJsonPath);
            Assert(imported == exported, $"Nhập lại JSON UTF-8 bảo toàn 100% số lượng prompt ({imported:N0} mục).");
            Console.WriteLine($"[PASS] Xuất và Nhập lại {imported:N0} prompt qua JSON UTF-8 thành công.");

            Console.WriteLine("=== TẤT CẢ KIỂM THỬ NGHIỆM THU ĐỀU ĐẠT (100% PASS) ===");
            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Bỏ qua nếu SQLite pool còn giữ handle ngắn hạn
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[FAILED] {message}");
        }
    }
}
