using System.Text.Json.Serialization;
using PromptNotebook.Helpers;

namespace PromptNotebook.Models;

/// <summary>
/// Thực thể Prompt lưu trữ trong bảng Prompts của SQLite và hỗ trợ xuất/nhập JSON UTF-8.
/// Khi hiển thị trong danh sách cột trái, chỉ tải metadata (Content = null) để tiết kiệm RAM.
/// Khi người dùng chọn prompt, Content mới được nạp đầy đủ từ SQLite.
/// </summary>
public sealed class PromptItem : ObservableObject
{
    private long _id;
    private string _title = string.Empty;
    private string? _content;
    private string _category = "Chung";
    private bool _isFavorite;
    private bool _isDeleted;
    private string _createdAt = string.Empty;
    private string _updatedAt = string.Empty;
    private string? _deletedAt;

    [JsonPropertyName("id")]
    public long Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    [JsonPropertyName("title")]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    [JsonPropertyName("content")]
    public string? Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    [JsonPropertyName("category")]
    public string Category
    {
        get => _category;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Chung" : value.Trim();
            SetProperty(ref _category, normalized);
        }
    }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteIcon));
            }
        }
    }

    [JsonPropertyName("isDeleted")]
    public bool IsDeleted
    {
        get => _isDeleted;
        set => SetProperty(ref _isDeleted, value);
    }

    [JsonPropertyName("createdAt")]
    public string CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    [JsonPropertyName("deletedAt")]
    public string? DeletedAt
    {
        get => _deletedAt;
        set => SetProperty(ref _deletedAt, value);
    }

    [JsonIgnore]
    public string FavoriteIcon => IsFavorite ? "★" : "☆";
}
