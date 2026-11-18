using ICSharpCode.AvalonEdit.Document;
using PromptNotebook.Helpers;

namespace PromptNotebook.Models;

public sealed class EditorTab : ObservableObject
{
    private bool _isDirty;

    public EditorTab(string name)
    {
        Name = name;
        Document = new TextDocument();
        Document.UndoStack.MarkAsOriginalFile();
        Document.UndoStack.PropertyChanged += (_, _) => IsDirty = !Document.UndoStack.IsOriginalFile;
    }

    public TextDocument Document { get; }
    public string Name { get; }
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
