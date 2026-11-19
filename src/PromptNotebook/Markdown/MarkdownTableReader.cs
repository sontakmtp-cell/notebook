using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PromptNotebook.Markdown;

internal sealed record MarkdownTableRow(
    int FirstLine, int LastLine, bool IsHeader, TableColumnAlign?[] Alignment, MarkdownCellPart[][] Cells);

internal readonly record struct MarkdownCellPart(string Text, InlineStyle Style, bool IsBreak = false);

internal static partial class MarkdownTableReader
{
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakTag();

    public static IEnumerable<MarkdownTableRow> Read(Table table, int lineCount, Func<int, string> sourceLine,
        MarkdownPipeline pipeline)
    {
        var rows = table.OfType<TableRow>().ToArray();
        var alignment = table.ColumnDefinitions.Select(column => column.Alignment).ToArray();
        if (rows.Length == 0 || alignment.Length == 0)
            yield break;

        var header = rows[0].Line;
        if (header + 1 >= lineCount)
            yield break;
        var recognized = rows.Select(row => row.Line).ToHashSet();
        yield return CreateRow(header, header + 1, true, SplitCells(Content(sourceLine(header))).Cells, alignment, pipeline);

        for (var first = header + 2; first < lineCount;)
        {
            var content = Content(sourceLine(first));
            var parsed = SplitCells(content);
            if (content.Length == 0 ||
                (!recognized.Contains(first) && !(content.StartsWith('|') && parsed.Cells.Count > 1)))
                yield break;

            var last = first;
            while (!parsed.Closed && last - first < 64 &&
                TryContinuation(last, lineCount, sourceLine, alignment.Length, parsed.Cells.Count < alignment.Length,
                    out var next, out var fragment))
            {
                content += " " + fragment;
                last = next;
                parsed = SplitCells(content);
            }

            yield return CreateRow(first, last, false, parsed.Cells, alignment, pipeline);
            first = last + 1;
        }
    }

    private static bool TryContinuation(int last, int lineCount, Func<int, string> sourceLine,
        int columns, bool needsColumns, out int next, out string fragment)
    {
        for (next = last + 1; next < lineCount && next <= last + 3; next++)
        {
            var raw = sourceLine(next);
            fragment = Content(raw);
            if (fragment.Length == 0)
                continue;

            var lineBreak = BreakTag().Match(fragment);
            var tail = SplitCells(fragment);
            return lineBreak is { Success: true, Index: 0 } ||
                (fragment.StartsWith('|') && char.IsWhiteSpace(raw[0]) &&
                 (needsColumns || fragment == "|") &&
                 (fragment == "|" || !tail.Closed || (next > last + 1 && tail.Cells.Count < columns)));
        }

        fragment = "";
        return false;
    }

    private static string Content(string raw)
    {
        var text = raw.AsSpan().Trim();
        while (text.StartsWith(">"))
            text = text[1..].TrimStart();
        return text.ToString();
    }

    private static (List<string> Cells, bool Closed) SplitCells(string content)
    {
        var cells = new List<string>();
        var start = content.StartsWith('|') ? 1 : 0;
        var escaped = false;
        var closed = false;
        for (var index = start; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '|' && !escaped)
            {
                cells.Add(content[start..index].Trim());
                start = index + 1;
                closed = start == content.Length;
            }
            escaped = character == '\\' && !escaped;
        }
        if (!closed)
            cells.Add(content[start..].Trim());
        return (cells, closed);
    }

    private static MarkdownTableRow CreateRow(int first, int last, bool header,
        List<string> cells, TableColumnAlign?[] alignment, MarkdownPipeline pipeline)
    {
        var content = new MarkdownCellPart[alignment.Length][];
        for (var column = 0; column < content.Length; column++)
            content[column] = column < cells.Count ? ReadCell(cells[column], pipeline) : [];
        return new MarkdownTableRow(first + 1, last + 1, header, alignment, content);
    }

    private static MarkdownCellPart[] ReadCell(string cell, MarkdownPipeline pipeline)
    {
        if (cell.Length == 0)
            return [];
        // A sentinel forces inline parsing even when the cell starts with Markdown block syntax.
        const string prefix = "x ";
        var source = prefix + cell;
        var parsed = Markdig.Markdown.Parse(source, pipeline);
        var parts = new List<MarkdownCellPart>();
        if (parsed.FirstOrDefault() is ParagraphBlock { Inline: { } inlines })
        {
            foreach (var inline in inlines)
                ReadInline(inline, source, parts, 0);
        }
        return parts.ToArray();
    }

    private static void ReadInline(Inline inline, string source, List<MarkdownCellPart> parts, InlineStyle style)
    {
        switch (inline)
        {
            case CodeInline code:
                parts.Add(new MarkdownCellPart(code.Content.Replace("\\|", "|", StringComparison.Ordinal), style | InlineStyle.Code));
                return;
            case EmphasisInline emphasis:
                style |= emphasis.DelimiterChar == '~' ? InlineStyle.Strike :
                    emphasis.DelimiterCount == 2 ? InlineStyle.Bold : InlineStyle.Italic;
                break;
            case LinkInline link:
                style |= InlineStyle.Link;
                if (link.IsImage)
                    AppendText("▧ ", style, parts);
                break;
            case AutolinkInline autolink:
                AppendText(autolink.Url, style | InlineStyle.Link, parts);
                return;
            case LiteralInline literal:
                var text = literal.Content.ToString();
                if (literal.Span.Start == 0)
                    text = text.Length > 2 ? text[2..] : "";
                AppendText(text, style, parts);
                return;
            case HtmlEntityInline entity:
                AppendText(entity.Transcoded.ToString(), style, parts);
                return;
            case LineBreakInline:
                parts.Add(new MarkdownCellPart("", style, IsBreak: true));
                return;
        }

        if (inline is ContainerInline container)
        {
            foreach (var child in container)
                ReadInline(child, source, parts, style);
        }
        else if (inline.Span.Start >= 2 && inline.Span.End < source.Length)
        {
            AppendText(source[inline.Span.Start..(inline.Span.End + 1)], style, parts);
        }
    }

    private static void AppendText(string text, InlineStyle style, List<MarkdownCellPart> parts)
    {
        var start = 0;
        foreach (Match match in BreakTag().Matches(text))
        {
            if (match.Index > start)
                parts.Add(new MarkdownCellPart(text[start..match.Index], style));
            parts.Add(new MarkdownCellPart("", style, IsBreak: true));
            start = match.Index + match.Length;
        }
        if (start < text.Length)
            parts.Add(new MarkdownCellPart(text[start..], style));
    }
}
