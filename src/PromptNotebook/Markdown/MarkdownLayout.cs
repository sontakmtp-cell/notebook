using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PromptNotebook.Markdown;

[Flags]
public enum InlineStyle { Bold = 1, Italic = 2, Strike = 4, Code = 8, Link = 16 }

public enum ProjectionKind { Hidden, Marker, TableDivider, Image, CopyCode }

public readonly record struct StyledRun(int Start, int Length, InlineStyle Style);

public readonly record struct Projection(int Start, int Length, ProjectionKind Kind, string Text = "", string? Target = null)
{
    public int End => Start + Length;
}

public sealed record MarkdownFence(string Language, string Code)
{
    public int CodeHash { get; } = StringComparer.Ordinal.GetHashCode(Code);
}

public sealed class MarkdownLine(string source)
{
    public string Source { get; } = source;
    public int Heading { get; set; }
    public int QuoteDepth { get; set; }
    public bool IsRule { get; set; }
    public bool IsTable { get; set; }
    public bool IsTableHeader { get; set; }
    public bool IsTableSeparator { get; set; }
    public MarkdownFence? Fence { get; set; }
    public int CodeLine { get; set; }
    public int CodeStart { get; set; }
    public List<StyledRun> Styles { get; } = [];
    public List<Projection> Projections { get; } = [];

    public bool HasSamePresentation(MarkdownLine? other) => other is not null &&
        Source == other.Source && Heading == other.Heading && QuoteDepth == other.QuoteDepth &&
        IsRule == other.IsRule && IsTable == other.IsTable && IsTableHeader == other.IsTableHeader &&
        IsTableSeparator == other.IsTableSeparator && CodeLine == other.CodeLine && CodeStart == other.CodeStart &&
        Fence?.CodeHash == other.Fence?.CodeHash && Fence?.Language == other.Fence?.Language &&
        Styles.SequenceEqual(other.Styles) && Projections.SequenceEqual(other.Projections);
}

public sealed class MarkdownLayout
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UsePipeTables()
        .DisableHtml()
        .Build();

    private readonly string _source;
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly Dictionary<int, MarkdownLine> _lines = [];

    private MarkdownLayout(string source)
    {
        _source = source;
        var starts = new List<int> { 0 };
        var ends = new List<int>();
        for (var position = 0; position < source.Length; position++)
        {
            if (source[position] is not ('\r' or '\n'))
                continue;
            ends.Add(position);
            if (source[position] == '\r' && position + 1 < source.Length && source[position + 1] == '\n')
                position++;
            starts.Add(position + 1);
        }

        ends.Add(source.Length);
        _starts = starts.ToArray();
        _ends = ends.ToArray();
    }

    public static MarkdownLayout Parse(string source)
    {
        var layout = new MarkdownLayout(source);
        var document = Markdig.Markdown.Parse(source, Pipeline);
        foreach (var block in document)
            layout.VisitBlock(block, 0);

        foreach (var (index, line) in layout._lines)
        {
            if (line.QuoteDepth > 0)
                layout.AddQuoteMarkers(index, line);
            line.Projections.Sort((left, right) => left.Start.CompareTo(right.Start));
        }
        return layout;
    }

    public MarkdownLine? GetLine(int oneBasedLineNumber) => _lines.GetValueOrDefault(oneBasedLineNumber - 1);

    private MarkdownLine Line(int index)
    {
        if (!_lines.TryGetValue(index, out var result))
        {
            result = new MarkdownLine(_source[_starts[index].._ends[index]]);
            _lines.Add(index, result);
        }
        return result;
    }

    private int LineIndex(int offset)
    {
        var found = Array.BinarySearch(_starts, Math.Clamp(offset, 0, _source.Length));
        return found >= 0 ? found : ~found - 1;
    }

    private void VisitBlock(Block block, int listDepth)
    {
        switch (block)
        {
            case HeadingBlock heading:
                VisitHeading(heading);
                break;
            case ThematicBreakBlock rule:
                Line(rule.Line).IsRule = true;
                Project(rule.Span.Start, rule.Span.End + 1, ProjectionKind.Hidden);
                break;
            case QuoteBlock quote:
                for (var line = quote.Line; line <= LineIndex(quote.Span.End); line++)
                    Line(line).QuoteDepth++;
                break;
            case ListBlock list:
                VisitList(list, listDepth);
                return;
            case FencedCodeBlock fence:
                VisitFence(fence);
                break;
            case Table table:
                VisitTable(table);
                break;
        }

        if (block is ContainerBlock container)
        {
            foreach (var child in container)
                VisitBlock(child, listDepth);
        }
        else if (block is LeafBlock { Inline: { } inline })
        {
            VisitInline(inline);
        }
    }

    private void VisitHeading(HeadingBlock heading)
    {
        var first = LineIndex(heading.Span.Start);
        var last = LineIndex(heading.Span.End);
        for (var row = first; row <= (heading.IsSetext ? last - 1 : last); row++)
            Line(row).Heading = heading.Level;

        if (heading.IsSetext)
        {
            if (last > first)
            {
                var startOfRule = _ends[last] - heading.HeaderCharCount;
                while (startOfRule > _starts[last] && char.IsWhiteSpace(_source[startOfRule - 1]))
                    startOfRule--;
                Project(startOfRule, _ends[last], ProjectionKind.Hidden);
            }
            return;
        }

        var start = heading.Span.Start;
        var afterMarker = start;
        while (afterMarker < _ends[first] && _source[afterMarker] == '#')
            afterMarker++;
        while (afterMarker < _ends[first] && char.IsWhiteSpace(_source[afterMarker]))
            afterMarker++;
        Project(start, afterMarker, ProjectionKind.Hidden);

        // The parser only trims closing hashes preceded by whitespace.
        var end = _ends[first];
        while (end > afterMarker && char.IsWhiteSpace(_source[end - 1]))
            end--;
        var hashes = end;
        while (hashes > afterMarker && _source[hashes - 1] == '#')
            hashes--;
        if (hashes < end && hashes > afterMarker && char.IsWhiteSpace(_source[hashes - 1]))
            Project(hashes - 1, _ends[first], ProjectionKind.Hidden);
    }

    private void VisitList(ListBlock list, int depth)
    {
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var child in list)
        {
            if (child is ListItemBlock item)
            {
                var offset = item.Span.Start;
                var end = offset;
                var endOfLine = _ends[item.Line];
                while (end < endOfLine && char.IsAsciiDigit(_source[end]))
                    end++;
                if (end < endOfLine && _source[end] is '-' or '+' or '*' or '.' or ')')
                {
                    end++;
                    while (end < endOfLine && _source[end] is ' ' or '\t')
                        end++;
                    var marker = list.IsOrdered ? $"{number}. " : (depth % 3) switch
                    {
                        0 => "• ",
                        1 => "◦ ",
                        _ => "▪ "
                    };
                    Project(offset, end, ProjectionKind.Marker, marker);
                }
                number++;
            }
            VisitBlock(child, depth + 1);
        }
    }

    private void VisitFence(FencedCodeBlock block)
    {
        var code = new StringBuilder();
        var sourceLines = block.Lines;
        for (var i = 0; i < sourceLines.Count; i++)
        {
            var line = sourceLines.Lines[i];
            code.Append(line.Slice.AsSpan());
            if (i < sourceLines.Count - 1)
                code.Append(_source.AsSpan(_ends[line.Line], _starts[line.Line + 1] - _ends[line.Line]));
        }

        var fence = new MarkdownFence(block.Info ?? "", code.ToString());
        var first = block.Line;
        Line(first).Fence = fence;
        Project(block.Span.Start, _ends[first], ProjectionKind.CopyCode, fence.Language);
        for (var i = 0; i < sourceLines.Count; i++)
        {
            var sourceLine = sourceLines.Lines[i];
            var line = Line(sourceLine.Line);
            line.Fence = fence;
            line.CodeLine = i + 1;
            // Slice.Start is relative to the original source except for expanded tabs.
            line.CodeStart = ReferenceEquals(sourceLine.Slice.Text, _source)
                ? Math.Clamp(sourceLine.Slice.Start - _starts[sourceLine.Line], 0, line.Source.Length)
                : Math.Clamp(sourceLine.Column, 0, line.Source.Length);
        }

        var last = LineIndex(block.Span.End);
        if (block.ClosingFencedCharCount > 0 && last > first)
        {
            Line(last).Fence = fence;
            var closingStart = block.Span.End + 1 - block.ClosingFencedCharCount;
            Project(closingStart, _ends[last], ProjectionKind.Hidden);
        }
    }

    private void AddQuoteMarkers(int index, MarkdownLine line)
    {
        var position = 0;
        for (var depth = 0; depth < line.QuoteDepth; depth++)
        {
            while (position < line.Source.Length && line.Source[position] is ' ' or '\t')
                position++;
            if (position >= line.Source.Length || line.Source[position] != '>')
                return;
            var end = position + 1;
            if (end < line.Source.Length && line.Source[end] is ' ' or '\t')
                end++;
            Project(_starts[index] + position, _starts[index] + end, ProjectionKind.Marker, "│ ");
            position = end;
        }
    }

    private void VisitTable(Table table)
    {
        var rows = table.OfType<TableRow>().ToArray();
        if (rows.Length == 0)
            return;

        var separator = rows[0].Line + 1;
        if (separator <= LineIndex(table.Span.End))
        {
            var line = Line(separator);
            line.IsTableSeparator = true;
            var first = _starts[separator];
            while (first < _ends[separator] && _source[first] is ' ' or '\t' or '>')
                first++;
            Project(first, _ends[separator], ProjectionKind.Hidden);
        }

        var pipesByRow = new int[rows.Length][];
        var widths = new int[table.ColumnDefinitions.Count];
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            var line = Line(row.Line);
            line.IsTable = true;
            line.IsTableHeader = row.IsHeader;
            var pipes = TablePipes(line.Source);
            pipesByRow[rowIndex] = pipes;
            var (_, firstColumn, start) = TableStart(line.Source, pipes);
            for (var i = firstColumn; i < pipes.Length && i - firstColumn < widths.Length; i++)
            {
                var column = i - firstColumn;
                widths[column] = Math.Max(widths[column], DisplayWidth(line.Source.AsSpan(start, pipes[i] - start).Trim()));
                start = pipes[i] + 1;
            }
        }

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var lineIndex = rows[rowIndex].Line;
            var line = Line(lineIndex).Source;
            var pipes = pipesByRow[rowIndex];
            var (leading, firstColumn, start) = TableStart(line, pipes);
            for (var i = 0; i < pipes.Length; i++)
            {
                var pipe = pipes[i];
                var replaceStart = pipe;
                var replaceEnd = pipe + 1;
                var padding = 0;
                if (!(leading && i == 0))
                {
                    while (replaceStart > start && line[replaceStart - 1] is ' ' or '\t')
                        replaceStart--;
                    var column = i - firstColumn;
                    if (column < widths.Length)
                        padding = Math.Clamp(widths[column] - DisplayWidth(line.AsSpan(start, pipe - start).Trim()) + 1, 1, 36);
                }
                while (replaceEnd < line.Length && line[replaceEnd] is ' ' or '\t')
                    replaceEnd++;
                var suffix = replaceEnd < line.Length && line[replaceEnd] != '|' ? " " : "";
                Project(_starts[lineIndex] + replaceStart, _starts[lineIndex] + replaceEnd,
                    ProjectionKind.TableDivider, new string('\u00a0', padding) + "│" + suffix);
                start = replaceEnd;
            }
        }
    }

    private static (bool Leading, int FirstColumn, int Start) TableStart(string source, int[] pipes)
    {
        var start = 0;
        while (start < source.Length && source[start] is ' ' or '\t' or '>')
            start++;
        var leading = pipes.Length > 0 && pipes[0] == start;
        return leading ? (true, 1, start + 1) : (false, 0, start);
    }

    private static int[] TablePipes(string line)
    {
        var pipes = new List<int>();
        var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '|' && !escaped)
                pipes.Add(i);
            escaped = line[i] == '\\' && !escaped;
        }
        return pipes.ToArray();
    }

    private static int DisplayWidth(ReadOnlySpan<char> text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += rune.Value is >= 0x2E80 and < 0xA500 or >= 0x1F300 ? 2 : 1;
        return width;
    }

    private void VisitInline(Inline inline)
    {
        var start = inline.Span.Start;
        var end = inline.Span.End + 1;
        switch (inline)
        {
            case EmphasisInline emphasis:
                var marker = emphasis.DelimiterCount;
                Style(start + marker, end - marker, emphasis.DelimiterChar == '~' ? InlineStyle.Strike :
                    marker == 2 ? InlineStyle.Bold : InlineStyle.Italic);
                Project(start, start + marker, ProjectionKind.Hidden);
                Project(end - marker, end, ProjectionKind.Hidden);
                break;
            case CodeInline code:
                Style(start + code.DelimiterCount, end - code.DelimiterCount, InlineStyle.Code);
                Project(start, start + code.DelimiterCount, ProjectionKind.Hidden);
                Project(end - code.DelimiterCount, end, ProjectionKind.Hidden);
                break;
            case LinkInline link when link.IsImage:
                var alt = string.Concat(link.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));
                if (LineIndex(start) == LineIndex(end - 1))
                {
                    Project(start, end, ProjectionKind.Image, alt, link.Url);
                    return;
                }
                break;
            case LinkInline { FirstChild: { } first, LastChild: { } last }:
                Style(first.Span.Start, last.Span.End + 1, InlineStyle.Link);
                Project(start, first.Span.Start, ProjectionKind.Hidden);
                Project(last.Span.End + 1, end, ProjectionKind.Hidden);
                break;
            case AutolinkInline:
                Style(start + 1, end - 1, InlineStyle.Link);
                Project(start, start + 1, ProjectionKind.Hidden);
                Project(end - 1, end, ProjectionKind.Hidden);
                break;
        }

        if (inline is ContainerInline container)
        {
            foreach (var child in container)
                VisitInline(child);
        }
    }

    private void Style(int start, int end, InlineStyle style)
    {
        var first = LineIndex(start);
        for (var index = first; index < _starts.Length && _starts[index] < end; index++)
        {
            var from = Math.Max(_starts[index], start);
            var to = Math.Min(_ends[index], end);
            if (to > from)
                Line(index).Styles.Add(new StyledRun(from - _starts[index], to - from, style));
        }
    }

    private void Project(int start, int end, ProjectionKind kind, string text = "", string? target = null)
    {
        if (start < 0 || end <= start || end > _source.Length)
            return;
        var index = LineIndex(start);
        if (end > _ends[index])
            return;

        var projection = new Projection(start - _starts[index], end - start, kind, text, target);
        var projections = Line(index).Projections;
        if (!projections.Any(p => p.Start < projection.End && p.End > projection.Start))
            projections.Add(projection);
    }
}
