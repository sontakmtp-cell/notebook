using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using PromptNotebook.Models;

namespace PromptNotebook.Services;

/// <summary>
/// Tầng truy cập dữ liệu SQLite thuần qua Microsoft.Data.Sqlite, không dùng ORM nặng.
/// Đảm bảo:
/// 1. Chỉ tải metadata (Id, Title, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt) cho danh sách.
/// 2. Chỉ tải Content đầy đủ của prompt đang chọn (GetPromptContent).
/// 3. Hỗ trợ tìm kiếm tức thì theo tên và nội dung với đầy đủ Unicode (tiếng Việt, Anh, Trung).
/// 4. Sao lưu cơ sở dữ liệu an toàn qua SQLite Online Backup API (SqliteConnection.BackupDatabase), giữ 7 bản gần nhất.
/// </summary>
public sealed class DatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseService(string? customDbPath = null)
    {
        _dbPath = customDbPath ?? AppPaths.DatabasePath;
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath => _dbPath;

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Đăng ký hàmเปรียบเทียบ chuỗi Unicode (CurrentCultureIgnoreCase / OrdinalIgnoreCase)
        // Giúp SQLite hỗ trợ tìm kiếm không phân biệt hoa thường cho cả tiếng Việt có dấu, tiếng Anh và tiếng Trung.
        conn.CreateFunction(
            "UNICODE_CONTAINS",
            (string? source, string? keyword) =>
            {
                if (string.IsNullOrEmpty(keyword)) return 1;
                if (string.IsNullOrEmpty(source)) return 0;
                return CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                    source,
                    keyword,
                    CompareOptions.IgnoreCase) >= 0 ? 1 : 0;
            });

        return conn;
    }

    /// <summary>
    /// Khởi tạo cơ sở dữ liệu, bảng Prompts và các chỉ mục tối ưu hiệu năng cho >= 5.000 prompt.
    /// </summary>
    public void InitializeDatabase()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Prompts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL DEFAULT '',
                Content TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT 'Chung',
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Prompts_ListFilter
                ON Prompts (IsDeleted, IsFavorite DESC, UpdatedAt DESC);

            CREATE INDEX IF NOT EXISTS IX_Prompts_Category
                ON Prompts (IsDeleted, Category);
        ";
        cmd.ExecuteNonQuery();

        SeedInitialPromptsIfEmpty(conn);
    }

    private static void SeedInitialPromptsIfEmpty(SqliteConnection conn)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(1) FROM Prompts;";
        var count = Convert.ToInt64(countCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var samples = new (string Title, string Category, bool IsFav, string Content)[]
        {
            (
                "Chuyên gia tối ưu mã nguồn C# .NET 10 và WPF hiệu năng cao (Kiểm tra hiển thị tiêu đề dài tự động xuống dòng trong danh sách cột bên trái mà không bị cắt chữ)",
                "Lập trình",
                true,
                "Bạn là kỹ sư phần mềm hệ thống chuyên sâu về C#, .NET 10 và WPF.\nHãy phân tích đoạn mã dưới đây theo các tiêu chí:\n1. Tối ưu cấp phát bộ nhớ (RAM) và tránh rò rỉ sự kiện.\n2. Đảm bảo VirtualizingStackPanel hoạt động mượt mà với hơn 5.000 mục.\n3. Sử dụng Microsoft.Data.Sqlite thuần với WAL mode.\n\n[Dán mã nguồn tại đây]"
            ),
            (
                "Bản dịch thuật đa ngôn ngữ Việt - Anh - Trung (中越英多语言专业翻译提示词 - 保持术语一致性与自然语感)",
                "Dịch thuật",
                true,
                "Hãy dịch văn bản kỹ thuật sau sang tiếng Việt, tiếng Anh và tiếng Trung giản thể (简体中文).\nYêu cầu:\n- Giữ nguyên thuật ngữ chuyên ngành AI và phần mềm.\n- Văn phong tự nhiên, chính xác 100% ngữ nghĩa.\n- Ví dụ tiếng Trung: 人工智能提示词笔记本 (Prompt Notebook) 支持高速搜索与本地安全存储。"
            ),
            (
                "Kịch bản phân tích yêu cầu sản phẩm và viết tài liệu kỹ thuật ngắn gọn",
                "Công việc",
                false,
                "Hãy tóm tắt đặc tả kỹ thuật sau thành danh sách kiểm tra nghiệm thu (Acceptance Criteria) rõ ràng, ngắn gọn, đi thẳng vào trọng tâm và không thêm tính năng dư thừa."
            )
        };

        using var tx = conn.BeginTransaction();
        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = @"
            INSERT INTO Prompts (Title, Content, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($title, $content, $category, $isFav, 0, $now, $now, NULL);
        ";

        var pTitle = insertCmd.Parameters.Add("$title", SqliteType.Text);
        var pContent = insertCmd.Parameters.Add("$content", SqliteType.Text);
        var pCategory = insertCmd.Parameters.Add("$category", SqliteType.Text);
        var pIsFav = insertCmd.Parameters.Add("$isFav", SqliteType.Integer);
        var pNow = insertCmd.Parameters.Add("$now", SqliteType.Text);
        pNow.Value = now;

        foreach (var s in samples)
        {
            pTitle.Value = s.Title;
            pContent.Value = s.Content;
            pCategory.Value = s.Category;
            pIsFav.Value = s.IsFav ? 1 : 0;
            insertCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// Truy vấn danh sách prompt cho cột trái.
    /// QUAN TRỌNG: Không SELECT cột Content để giảm tối đa RAM và tăng tốc độ phản hồi (< 200ms với 5.000+ prompt).
    /// </summary>
    public List<PromptItem> QueryPromptList(
        string? searchKeyword = null,
        string? categoryFilter = null,
        bool onlyFavorites = false,
        bool showTrash = false)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();

        var hasKeyword = !string.IsNullOrWhiteSpace(searchKeyword);
        var hasCategory = !string.IsNullOrWhiteSpace(categoryFilter) &&
                          !string.Equals(categoryFilter, "Tất cả danh mục", StringComparison.OrdinalIgnoreCase);

        var sql = @"
            SELECT Id, Title, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt
            FROM Prompts
            WHERE IsDeleted = $isDeleted
        ";

        cmd.Parameters.AddWithValue("$isDeleted", showTrash ? 1 : 0);

        if (onlyFavorites && !showTrash)
        {
            sql += " AND IsFavorite = 1";
        }

        if (hasCategory)
        {
            sql += " AND Category = $category";
            cmd.Parameters.AddWithValue("$category", categoryFilter!.Trim());
        }

        if (hasKeyword)
        {
            var kw = searchKeyword!.Trim();
            // Kiểm tra nhanh bằng INSTR cho chữ hoa/thường chính xác (cực nhanh cho tiếng Trung/ASCII)
            // kết hợp UNICODE_CONTAINS cho tìm kiếm không phân biệt hoa thường tiếng Việt/Anh/Trung
            sql += " AND (INSTR(Title, $kw) > 0 OR INSTR(Content, $kw) > 0 OR UNICODE_CONTAINS(Title, $kw) = 1 OR UNICODE_CONTAINS(Content, $kw) = 1)";
            cmd.Parameters.AddWithValue("$kw", kw);
        }

        sql += " ORDER BY IsFavorite DESC, UpdatedAt DESC, Id DESC;";
        cmd.CommandText = sql;

        var results = new List<PromptItem>(256);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PromptItem
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Content = null, // Chỉ tải khi người dùng chọn prompt
                Category = reader.GetString(2),
                IsFavorite = reader.GetInt64(3) != 0,
                IsDeleted = reader.GetInt64(4) != 0,
                CreatedAt = reader.GetString(5),
                UpdatedAt = reader.GetString(6),
                DeletedAt = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return results;
    }

    /// <summary>
    /// Chỉ tải nội dung đầy đủ (Content) của một prompt khi được chọn.
    /// </summary>
    public string GetPromptContent(long id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Content FROM Prompts WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        var result = cmd.ExecuteScalar();
        return result as string ?? string.Empty;
    }

    /// <summary>
    /// Lấy danh sách tất cả các danh mục hiện có trong DB.
    /// </summary>
    public List<string> GetCategories()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT Category
            FROM Prompts
            WHERE IsDeleted = 0 AND TRIM(Category) <> ''
            ORDER BY Category COLLATE NOCASE ASC;
        ";

        var list = new List<string> { "Chung", "Công việc", "Lập trình", "Dịch thuật", "Viết lách", "Hình ảnh / Video" };
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var cat = reader.GetString(0).Trim();
            if (!string.IsNullOrEmpty(cat) && !list.Contains(cat, StringComparer.CurrentCultureIgnoreCase))
            {
                list.Add(cat);
            }
        }
        return list;
    }

    /// <summary>
    /// Tạo mới một prompt và trả về thực thể vừa tạo.
    /// </summary>
    public PromptItem CreatePrompt(string title = "Prompt mới", string content = "", string category = "Chung", bool isFavorite = false)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "Chung" : category.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Prompt không tên" : title.Trim();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Prompts (Title, Content, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($title, $content, $category, $isFav, 0, $now, $now, NULL);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$title", normalizedTitle);
        cmd.Parameters.AddWithValue("$content", content ?? string.Empty);
        cmd.Parameters.AddWithValue("$category", normalizedCategory);
        cmd.Parameters.AddWithValue("$isFav", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", now);

        var id = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        return new PromptItem
        {
            Id = id,
            Title = normalizedTitle,
            Content = content ?? string.Empty,
            Category = normalizedCategory,
            IsFavorite = isFavorite,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };
    }

    /// <summary>
    /// Cập nhật thông tin và nội dung của prompt (dùng cho tự động lưu 700ms, lưu khi đổi prompt hoặc đóng app).
    /// </summary>
    public string UpdatePrompt(long id, string title, string content, string category, bool isFavorite)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Prompt không tên" : title.Trim();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "Chung" : category.Trim();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Prompts
            SET Title = $title,
                Content = $content,
                Category = $category,
                IsFavorite = $isFav,
                UpdatedAt = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", normalizedTitle);
        cmd.Parameters.AddWithValue("$content", content ?? string.Empty);
        cmd.Parameters.AddWithValue("$category", normalizedCategory);
        cmd.Parameters.AddWithValue("$isFav", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();

        return now;
    }

    /// <summary>
    /// Chỉ cập nhật tên hoặc trạng thái yêu thích nhanh trên danh sách mà không ghi đè Content.
    /// </summary>
    public string UpdateMetadataOnly(long id, string title, string category, bool isFavorite)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Prompt không tên" : title.Trim();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "Chung" : category.Trim();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Prompts
            SET Title = $title,
                Category = $category,
                IsFavorite = $isFav,
                UpdatedAt = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", normalizedTitle);
        cmd.Parameters.AddWithValue("$category", normalizedCategory);
        cmd.Parameters.AddWithValue("$isFav", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();

        return now;
    }

    /// <summary>
    /// Nhân bản một prompt đang có (tự động đọc Content từ DB để sao chép đầy đủ).
    /// </summary>
    public PromptItem? DuplicatePrompt(long sourceId)
    {
        using var conn = CreateConnection();
        using var getCmd = conn.CreateCommand();
        getCmd.CommandText = @"
            SELECT Title, Content, Category, IsFavorite
            FROM Prompts
            WHERE Id = $id
            LIMIT 1;
        ";
        getCmd.Parameters.AddWithValue("$id", sourceId);

        using var reader = getCmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var title = reader.GetString(0) + " (Bản sao)";
        var content = reader.GetString(1);
        var category = reader.GetString(2);
        var isFav = reader.GetInt64(3) != 0;
        reader.Close();

        return CreatePrompt(title, content, category, isFav);
    }

    /// <summary>
    /// Chuyển prompt vào thùng rác (Soft Delete).
    /// </summary>
    public void MoveToTrash(long id)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Prompts
            SET IsDeleted = 1,
                DeletedAt = $now,
                UpdatedAt = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Khôi phục prompt từ thùng rác.
    /// </summary>
    public void RestoreFromTrash(long id)
    {
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Prompts
            SET IsDeleted = 0,
                DeletedAt = NULL,
                UpdatedAt = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Xóa vĩnh viễn một prompt khỏi thùng rác.
    /// </summary>
    public void PermanentlyDelete(long id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Prompts WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Dọn sạch thùng rác.
    /// </summary>
    public int EmptyTrash()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Prompts WHERE IsDeleted = 1;";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Lấy toàn bộ dữ liệu (bao gồm cả Content) để xuất ra file JSON UTF-8.
    /// </summary>
    public List<PromptItem> GetAllPromptsForExport(bool includeDeleted = false)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT Id, Title, Content, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt FROM Prompts ORDER BY Id ASC;"
            : "SELECT Id, Title, Content, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt FROM Prompts WHERE IsDeleted = 0 ORDER BY Id ASC;";

        var list = new List<PromptItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PromptItem
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Content = reader.GetString(2),
                Category = reader.GetString(3),
                IsFavorite = reader.GetInt64(4) != 0,
                IsDeleted = reader.GetInt64(5) != 0,
                CreatedAt = reader.GetString(6),
                UpdatedAt = reader.GetString(7),
                DeletedAt = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return list;
    }

    /// <summary>
    /// Nhập hàng loạt prompt trong một Transaction nguyên tử (hỗ trợ nhập JSON hoặc tạo 5.000+ prompt thử nghiệm).
    /// </summary>
    public int BulkInsertPrompts(IEnumerable<PromptItem> items)
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO Prompts (Title, Content, Category, IsFavorite, IsDeleted, CreatedAt, UpdatedAt, DeletedAt)
            VALUES ($title, $content, $category, $isFav, $isDel, $createdAt, $updatedAt, $deletedAt);
        ";

        var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
        var pContent = cmd.Parameters.Add("$content", SqliteType.Text);
        var pCategory = cmd.Parameters.Add("$category", SqliteType.Text);
        var pIsFav = cmd.Parameters.Add("$isFav", SqliteType.Integer);
        var pIsDel = cmd.Parameters.Add("$isDel", SqliteType.Integer);
        var pCreated = cmd.Parameters.Add("$createdAt", SqliteType.Text);
        var pUpdated = cmd.Parameters.Add("$updatedAt", SqliteType.Text);
        var pDeleted = cmd.Parameters.Add("$deletedAt", SqliteType.Text);

        var count = 0;
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        foreach (var item in items)
        {
            pTitle.Value = string.IsNullOrWhiteSpace(item.Title) ? "Prompt không tên" : item.Title.Trim();
            pContent.Value = item.Content ?? string.Empty;
            pCategory.Value = string.IsNullOrWhiteSpace(item.Category) ? "Chung" : item.Category.Trim();
            pIsFav.Value = item.IsFavorite ? 1 : 0;
            pIsDel.Value = item.IsDeleted ? 1 : 0;
            pCreated.Value = string.IsNullOrWhiteSpace(item.CreatedAt) ? now : item.CreatedAt;
            pUpdated.Value = string.IsNullOrWhiteSpace(item.UpdatedAt) ? now : item.UpdatedAt;
            pDeleted.Value = item.DeletedAt is null ? DBNull.Value : item.DeletedAt;

            cmd.ExecuteNonQuery();
            count++;
        }

        tx.Commit();
        return count;
    }

    /// <summary>
    /// Sao lưu an toàn cơ sở dữ liệu bằng cơ chế Online Backup API của SQLite (SqliteConnection.BackupDatabase)
    /// và tự động giữ đúng 7 bản sao lưu gần nhất.
    /// </summary>
    public string PerformSafeBackup(string? customBackupDir = null, int maxBackupsToKeep = 7)
    {
        var backupDir = customBackupDir ?? AppPaths.BackupsDirectory;
        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var backupFilePath = Path.Combine(backupDir, $"prompts_backup_{timestamp}.db");

        using (var sourceConn = CreateConnection())
        {
            var destConnString = new SqliteConnectionStringBuilder
            {
                DataSource = backupFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            using var destConn = new SqliteConnection(destConnString);
            destConn.Open();
            sourceConn.BackupDatabase(destConn);
        }

        PruneOldBackups(backupDir, maxBackupsToKeep);
        return backupFilePath;
    }

    private static void PruneOldBackups(string backupDir, int maxBackupsToKeep)
    {
        var files = new DirectoryInfo(backupDir)
            .GetFiles("prompts_backup_*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .ToList();

        if (files.Count <= maxBackupsToKeep)
            return;

        foreach (var oldFile in files.Skip(maxBackupsToKeep))
        {
            try
            {
                oldFile.Delete();
            }
            catch
            {
                // Bỏ qua nếu file đang bận
            }
        }
    }
}
