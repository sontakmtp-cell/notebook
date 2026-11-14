using System.IO;
using Microsoft.Data.Sqlite;
using PromptNotebook.Models;
using PromptNotebook.Services;
using PromptNotebook.ViewModels;

namespace PromptNotebook.Tests;

internal static class PersonalFeaturesTests
{
    public static void Run(string testRoot)
    {
        var envPath = Path.Combine(testRoot, ".env");
        File.WriteAllText(envPath, "mật-khẩu=cá-nhân\n");
        var password = new PasswordService(envPath);
        Check(password.Validate("mật-khẩu=cá-nhân") && !password.Validate("sai") && !password.Validate(""),
            "File một dòng dùng đúng mật khẩu, kể cả dấu bằng; từ chối sai và rỗng.");
        File.WriteAllText(envPath, "# cấu hình\nAPP_PASSWORD=\"Tiếng Việt #123=\"\n");
        Check(new PasswordService(envPath).Validate("Tiếng Việt #123="), "Hỗ trợ APP_PASSWORD và dấu nháy.");
        File.WriteAllText(envPath, "APP_PASSWORD=\n");
        ExpectInvalid(() => new PasswordService(envPath));
        ExpectInvalid(() => new PasswordService(envPath + ".missing"));

        var dbPath = Path.Combine(testRoot, "personal-features.db");
        var db = new DatabaseService(dbPath);
        db.InitializeDatabase();
        var first = db.CreatePrompt("Một", "Nội dung một", "Riêng");
        var second = db.CreatePrompt("Hai", "Nội dung hai", "Ẩn", true);
        var third = db.CreatePrompt("Ba", "Nội dung ba", "Riêng");
        var oldOrder = db.QueryPromptList().Select(p => p.Id).ToArray();
        db.MovePrompt(first.Id, third.Id, false);
        var reordered = db.QueryPromptList().Select(p => p.Id).ToArray();
        Check(reordered.Take(3).SequenceEqual(new[] { first.Id, third.Id, second.Id }),
            "Kéo lên trước mục khác, vượt qua mục yêu thích và mục bị ẩn.");
        db.UpdatePrompt(first.Id, first.Title, "Đã sửa", first.Category, false);
        var reopened = new DatabaseService(dbPath);
        reopened.InitializeDatabase();
        Check(reopened.QueryPromptList().Select(p => p.Id).SequenceEqual(reordered),
            "Sửa nội dung và mở lại app không đổi thứ tự đã kéo.");
        Check(reopened.QueryPromptList(categoryFilter: "Riêng").Select(p => p.Id)
            .SequenceEqual(new[] { first.Id, third.Id }), "Bộ lọc giữ đúng thứ tự thủ công.");
        reopened.MovePrompt(first.Id, second.Id, true);
        Check(reopened.QueryPromptList().Select(p => p.Id).SequenceEqual(oldOrder), "Kéo xuống sau mục khác.");

        var vm = new MainViewModel(reopened, new SettingsService(Path.Combine(testRoot, "personal-settings.json")),
            new JsonExportImportService(), new AppSettings());
        vm.Initialize(first.Id);
        vm.EditingContent = "Sửa chưa kịp lưu";
        vm.MovePrompt(vm.Prompts.Single(p => p.Id == first.Id), vm.Prompts.Single(p => p.Id == third.Id), false);
        Check(reopened.GetPromptContent(first.Id) == "Sửa chưa kịp lưu" && vm.SelectedPrompt?.Id == first.Id,
            "Kéo thả lưu chỉnh sửa đang chờ và giữ mục đang chọn.");

        var json = new JsonExportImportService();
        var jsonPath = Path.Combine(testRoot, "ordered.json");
        json.ExportToJsonFile(reopened, jsonPath);
        var imported = new DatabaseService(Path.Combine(testRoot, "ordered-import.db"));
        imported.InitializeDatabase();
        var count = json.ImportFromJsonFile(imported, jsonPath);
        Check(imported.QueryPromptList().Take(count).Select(p => p.Title)
            .SequenceEqual(reopened.QueryPromptList().Select(p => p.Title)), "Xuất rồi nhập JSON giữ thứ tự prompt.");

        // Mô phỏng file dữ liệu cũ trước khi có cột sắp xếp.
        var legacyPath = Path.Combine(testRoot, "legacy.db");
        var legacy = new DatabaseService(legacyPath);
        legacy.InitializeDatabase();
        using (var conn = new SqliteConnection($"Data Source={legacyPath};Pooling=False"))
        {
            conn.Open();
            using var command = conn.CreateCommand();
            command.CommandText = "DROP INDEX IX_Prompts_SortOrder; ALTER TABLE Prompts DROP COLUMN SortOrder;";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT Id FROM Prompts ORDER BY IsFavorite DESC, UpdatedAt DESC, Id DESC;";
            using var reader = command.ExecuteReader();
            var expected = new List<long>();
            while (reader.Read()) expected.Add(reader.GetInt64(0));
            reader.Close();
            legacy.InitializeDatabase();
            Check(legacy.QueryPromptList().Select(p => p.Id).SequenceEqual(expected),
                "Nâng cấp dữ liệu cũ giữ nguyên prompt và thứ tự ban đầu.");
        }
        Console.WriteLine("[PASS] Mật khẩu .env; kéo thả, lưu thứ tự, bộ lọc, JSON và nâng cấp dữ liệu cũ.");
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("[FAILED] Phải chặn đăng nhập nếu .env thiếu hoặc mật khẩu rỗng.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[FAILED] " + message);
    }
}
