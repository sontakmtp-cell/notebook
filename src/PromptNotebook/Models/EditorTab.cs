using ICSharpCode.AvalonEdit.Document;
using PromptNotebook.Helpers;
using PromptNotebook.Services;
using System.IO;

namespace PromptNotebook.Models;

public sealed class EditorTab : ObservableObject
{
    private bool _isDirty;
    private string? _path;
    private readonly string _untitledName;
    private FileFormat _format;

    public EditorTab(string name, FileFormat format = FileFormat.Txt, string? path = null,
        string text = "", TextEncoding encoding = TextEncoding.Utf8)
    {
        _untitledName = name;
        _path = path;
        _format = format;
        Encoding = encoding;
        Document = new TextDocument(text);
        Document.UndoStack.MarkAsOriginalFile();
        Document.UndoStack.PropertyChanged += (_, _) => IsDirty = !Document.UndoStack.IsOriginalFile;
    }

    public TextDocument Document { get; }
    public string Name => _path is null ? _untitledName + Format.Extension() : Path.GetFileName(_path);
    public string ToolTip => _path ?? Name;
    public string? FilePath => _path;
    internal Guid RecoveryId { get; init; } = Guid.NewGuid();
    internal string? RecoveredFrom { get; init; }
    internal string? RecoveryName { get; init; }
    internal FileStamp? SavedStamp { get; set; }
    public FileFormat Format => _format;
    public TextEncoding Encoding { get; }

    public void CompleteSave(string path, ITextSourceVersion version)
    {
        _path = path;
        _format = FileFormats.FromPath(path);
        if (ReferenceEquals(version, Document.Version))
            Document.UndoStack.MarkAsOriginalFile();
        else
            Document.UndoStack.DiscardOriginalFileMarker();

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ToolTip));
    }
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public int Caret { get; set; }
    public int SelectionAnchor { get; set; }
    public int SelectionEnd { get; set; }
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }
}
