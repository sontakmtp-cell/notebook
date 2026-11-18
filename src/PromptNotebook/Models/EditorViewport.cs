using System.Windows.Controls.Primitives;
using ICSharpCode.AvalonEdit.Rendering;

namespace PromptNotebook.Models;

public readonly record struct EditorViewport(int TopLine, double LineOffset, double HorizontalOffset)
{
    internal static EditorViewport Capture(TextView view)
    {
        var offset = view.ScrollOffset;
        var line = view.GetDocumentLineByVisualTop(offset.Y).LineNumber;
        return new EditorViewport(line,
            Math.Max(0, offset.Y - view.GetVisualTopByDocumentLine(line)), offset.X);
    }

    internal void Restore(TextView view)
    {
        var line = Math.Clamp(TopLine, 1, view.Document.LineCount);
        var clip = 0d;
        if (double.IsFinite(LineOffset) && LineOffset > 0)
        {
            var rendered = view.GetOrConstructVisualLine(view.Document.GetLineByNumber(line));
            clip = Math.Min(LineOffset, Math.Max(0, rendered.Height - 0.5));
        }

        var scroll = (IScrollInfo)view;
        scroll.SetVerticalOffset(view.GetVisualTopByDocumentLine(line) + clip);
        scroll.SetHorizontalOffset(double.IsFinite(HorizontalOffset) ? Math.Max(0, HorizontalOffset) : 0);
    }
}
