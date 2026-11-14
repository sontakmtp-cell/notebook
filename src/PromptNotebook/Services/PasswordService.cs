using System.IO;

namespace PromptNotebook.Services;

public sealed class PasswordService
{
    private readonly string _password;

    public PasswordService(string envPath)
    {
        if (!File.Exists(envPath))
            throw new InvalidOperationException("Không tìm thấy .env. Đặt file .env chứa mật khẩu cạnh PromptNotebook.exe.");

        var lines = File.ReadAllLines(envPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        // Hỗ trợ file hiện tại chứa một dòng mật khẩu, hoặc dạng KEY=value thông thường.
        var assignment = lines.FirstOrDefault(line =>
            line.StartsWith("PASSWORD=", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("APP_PASSWORD=", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("PROMPT_NOTEBOOK_PASSWORD=", StringComparison.OrdinalIgnoreCase));
        var value = assignment is not null
            ? assignment[(assignment.IndexOf('=') + 1)..].Trim()
            : lines.Length == 1 ? lines[0] : string.Empty;

        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
            value = value[1..^1];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Chưa có mật khẩu trong .env. Điền một dòng mật khẩu hoặc APP_PASSWORD=mật_khẩu.");

        _password = value;
    }

    public bool Validate(string password) => string.Equals(_password, password, StringComparison.Ordinal);
}
