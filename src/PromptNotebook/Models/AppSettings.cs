namespace PromptNotebook.Models;

public sealed class AppSettings
{
    public bool IsDarkMode { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    public double EditorFontSize { get; set; } = 16;
    public double WindowWidth { get; set; } = 1080;
    public double WindowHeight { get; set; } = 720;
    public bool IsMaximized { get; set; }
    public List<string> RecentFiles { get; set; } = [];
}
