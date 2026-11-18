using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;

namespace PromptNotebook.Helpers;

public sealed class JsonIndentation : IIndentationStrategy
{
    public static JsonIndentation Instance { get; } = new();

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        if (line.PreviousLine is not { } previous)
            return;

        var previousText = document.GetText(previous).TrimEnd();
        var indent = TextUtilities.GetWhitespaceAfter(document, previous.Offset).Length;
        if (previousText.EndsWith('{') || previousText.EndsWith('['))
            indent += 2;

        var whitespace = TextUtilities.GetWhitespaceAfter(document, line.Offset);
        if (whitespace.EndOffset < line.EndOffset && document.GetCharAt(whitespace.EndOffset) is '}' or ']')
            indent = Math.Max(0, indent - 2);

        document.Replace(whitespace.Offset, whitespace.Length, new string(' ', indent));
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        using (document.RunUpdate())
        {
            for (var number = beginLine; number <= endLine; number++)
                IndentLine(document, document.GetLineByNumber(number));
        }
    }

    public static void AlignClosingBracket(TextDocument document, int offset)
    {
        var line = document.GetLineByOffset(offset);
        var whitespace = TextUtilities.GetWhitespaceAfter(document, line.Offset);
        if (line.PreviousLine is { } previous && offset == whitespace.EndOffset)
        {
            var previousIndent = TextUtilities.GetWhitespaceAfter(document, previous.Offset).Length;
            var previousText = document.GetText(previous).TrimEnd();
            // The previous line could be the end of an inner container. In either case,
            // the newly typed closing bracket moves out one indentation level.
            var indent = previousText.EndsWith('{') || previousText.EndsWith('[')
                ? previousIndent : Math.Max(0, whitespace.Length - 2);
            if (indent != whitespace.Length)
                document.Replace(whitespace.Offset, whitespace.Length, new string(' ', indent));
        }
    }
}
