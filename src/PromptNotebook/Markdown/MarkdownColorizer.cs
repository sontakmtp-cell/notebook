using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using PromptNotebook.Helpers;

namespace PromptNotebook.Markdown;

internal sealed class MarkdownColorizer(MarkdownPresentation presenter) : DocumentColorizingTransformer
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly double[] HeadingScale = [1, 1.95, 1.6, 1.38, 1.22, 1.12, 1.04];

    private readonly Dictionary<MarkdownFence, DocumentHighlighter?> _code = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Color, Brush> _adaptedColors = [];
    private bool _dark;

    public void SetTheme(bool dark)
    {
        if (_dark == dark)
            return;
        _dark = dark;
        _adaptedColors.Clear();
        ResetFences();
    }

    public void ResetFences()
    {
        foreach (var highlighter in _code.Values)
            highlighter?.Dispose();
        _code.Clear();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (presenter.LayoutFor(line) is not { TableRow: null } layout)
            return;

        if (layout.Heading > 0)
        {
            ChangeLinePart(line.Offset, line.EndOffset, element =>
            {
                var properties = element.TextRunProperties;
                properties.SetFontRenderingEmSize(properties.FontRenderingEmSize * HeadingScale[layout.Heading]);
                SetTypeface(element, weight: FontWeights.Bold);
            });
        }
        if (layout.Fence is not null)
            ChangeLinePart(line.Offset, line.EndOffset, element => SetTypeface(element, Mono));
        if (layout.QuoteDepth > 0)
            ChangeLinePart(line.Offset, line.EndOffset,
                element => element.TextRunProperties.SetForegroundBrush(presenter.Brush("SecondaryTextBrush")));

        foreach (var style in layout.Styles)
            ChangeLinePart(line.Offset + style.Start, line.Offset + style.Start + style.Length,
                element => ApplyInline(element, style.Style));

        foreach (var projection in layout.Projections)
        {
            if (presenter.IsEditing(line) || projection.Kind is ProjectionKind.Marker or ProjectionKind.Image)
                ChangeLinePart(line.Offset + projection.Start, line.Offset + projection.End,
                    element => element.TextRunProperties.SetForegroundBrush(presenter.Brush("SecondaryTextBrush")));
        }

        if (layout.Fence is { } fence && layout.CodeLine > 0)
            HighlightCode(line, layout, fence);
    }

    private void ApplyInline(VisualLineElement element, InlineStyle style)
    {
        var props = element.TextRunProperties;
        switch (style)
        {
            case InlineStyle.Bold:
                SetTypeface(element, weight: FontWeights.Bold);
                break;
            case InlineStyle.Italic:
                SetTypeface(element, style: FontStyles.Italic);
                break;
            case InlineStyle.Strike:
                AddDecorations(element, TextDecorations.Strikethrough);
                break;
            case InlineStyle.Link:
                props.SetForegroundBrush(presenter.Brush("AccentBrush"));
                AddDecorations(element, TextDecorations.Underline);
                break;
            case InlineStyle.Code:
                SetTypeface(element, Mono);
                element.BackgroundBrush = presenter.Brush("MarkdownCodeBrush");
                break;
        }
    }

    private static void AddDecorations(VisualLineElement element, TextDecorationCollection added)
    {
        var props = element.TextRunProperties;
        var decorations = props.TextDecorations?.Clone() ?? new TextDecorationCollection();
        decorations.Add(added);
        props.SetTextDecorations(decorations);
    }

    private static void SetTypeface(VisualLineElement element, FontFamily? family = null,
        FontWeight? weight = null, FontStyle? style = null)
    {
        var properties = element.TextRunProperties;
        var current = properties.Typeface;
        properties.SetTypeface(new Typeface(family ?? current.FontFamily, style ?? current.Style,
            weight ?? current.Weight, current.Stretch));
    }

    private void HighlightCode(DocumentLine line, MarkdownLine layout, MarkdownFence fence)
    {
        if (!_code.TryGetValue(fence, out var highlighter))
        {
            var definition = Definition(fence.Language);
            if (_code.Count >= 16)
                ResetFences();

            highlighter = definition is null || fence.Code.Length > 200_000 ? null :
                new DocumentHighlighter(new TextDocument(fence.Code), definition);
            _code.Add(fence, highlighter);
        }
        if (highlighter is null || layout.CodeLine > highlighter.Document.LineCount)
            return;

        var codeLine = highlighter.HighlightLine(layout.CodeLine);
        foreach (var section in codeLine.Sections)
        {
            if (section.Color.Foreground?.GetBrush(CurrentContext) is not { } brush)
                continue;
            var start = line.Offset + layout.CodeStart + section.Offset - codeLine.DocumentLine.Offset;
            var end = Math.Min(start + section.Length, line.EndOffset);
            if (start >= line.Offset && start < end)
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(AdaptColor(brush)));
        }
    }

    private Brush AdaptColor(Brush candidate)
    {
        if (candidate is not SolidColorBrush solid)
            return candidate;

        var color = solid.Color;
        if (_adaptedColors.TryGetValue(color, out var existing))
            return existing;

        var luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
        var mix = _dark ? Math.Clamp((165 - luminance) / (255 - luminance), 0, 1) :
            Math.Clamp((luminance - 108) / Math.Max(luminance, 1), 0, 1);
        if (mix <= 0)
            return candidate;

        var goal = _dark ? 255 : 0;
        var adapted = new SolidColorBrush(Color.FromRgb(
            (byte)(color.R + (goal - color.R) * mix),
            (byte)(color.G + (goal - color.G) * mix),
            (byte)(color.B + (goal - color.B) * mix)));
        adapted.Freeze();
        _adaptedColors[color] = adapted;
        return adapted;
    }

    private IHighlightingDefinition? Definition(string language)
    {
        if (language.Equals("json", StringComparison.OrdinalIgnoreCase))
            return JsonHighlighting.ForTheme(_dark);
        var extension = language.ToLowerInvariant() switch
        {
            "csharp" or "c#" => ".cs",
            "javascript" or "typescript" or "ts" => ".js",
            "python" => ".py",
            "powershell" or "pwsh" => ".ps1",
            "c++" => ".cpp",
            "vbnet" => ".vb",
            _ => "." + language
        };
        return HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }
}
