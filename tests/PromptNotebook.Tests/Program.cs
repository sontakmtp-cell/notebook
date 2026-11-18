using ICSharpCode.AvalonEdit.Document;
using PromptNotebook.Models;

var first = new EditorTab("One");
var second = new EditorTab("Two");
first.Document.Insert(0, "Tiếng Việt · 中文 · 😀");
Assert(first.IsDirty && !second.IsDirty && second.Document.TextLength == 0, "Independent documents");
first.Document.UndoStack.Undo();
Assert(!first.IsDirty && first.Document.TextLength == 0, "Undo returns to saved state");
first.Document.UndoStack.Redo();
Assert(first.IsDirty && first.Document.Text.Contains("Tiếng Việt"), "Redo restores Unicode text");
first.Document.UndoStack.MarkAsOriginalFile();
Assert(!first.IsDirty, "Saved state clears dirty indicator");

Console.WriteLine("[PASS] Tab documents, Unicode, undo, redo and saved state");

static void Assert(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}
