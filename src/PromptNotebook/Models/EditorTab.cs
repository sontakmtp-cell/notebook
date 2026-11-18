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
        : this(name, format, path, new TextDocument(text), encoding, CountCharacters(text))
    {
    }

    internal EditorTab(string name, FileFormat format, string? path, TextDocument document,
        TextEncoding encoding, int characterCount)
    {
        _untitledName = name;
        _path = path;
        _format = format;
        Encoding = encoding;
        Document = document;
        CharacterCount = characterCount;
        Document.Changing += CountEdit;
        Document.UndoStack.MarkAsOriginalFile();
        Document.UndoStack.PropertyChanged += (_, _) => IsDirty = !Document.UndoStack.IsOriginalFile;
    }

    public TextDocument Document { get; }
    public int CharacterCount { get; private set; }
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
    internal EditorViewport Viewport { get; set; }

    internal static int CountCharacters(ReadOnlySpan<char> text) =>
        text.Length - CountSurrogatePairs(text, '\0', '\0');

    private void CountEdit(object? sender, DocumentChangeEventArgs e)
    {
        var changeEnd = e.Offset + e.RemovalLength;
        var before = e.Offset > 0 ? Document.GetCharAt(e.Offset - 1) : '\0';
        var after = changeEnd < Document.TextLength ? Document.GetCharAt(changeEnd) : '\0';
        var removedPairs = CountSurrogatePairs(e.RemovedText.Text, before, after);
        var insertedPairs = CountSurrogatePairs(e.InsertedText.Text, before, after);
        CharacterCount += e.InsertionLength - e.RemovalLength + removedPairs - insertedPairs;
    }

    private static int CountSurrogatePairs(ReadOnlySpan<char> text, char before, char after)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (char.IsHighSurrogate(before) && char.IsLowSurrogate(character))
                count++;
            before = character;
        }
        return count + (char.IsHighSurrogate(before) && char.IsLowSurrogate(after) ? 1 : 0);
    }
}
