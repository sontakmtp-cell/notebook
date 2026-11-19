using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Extensions.Tables;

namespace PromptNotebook.Markdown;

internal static class MarkdownTableView
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    public static Grid Create(MarkdownTableRow row, double width, double fontSize, Func<string, Brush> brush)
    {
        var grid = new Grid
        {
            Width = width,
            UseLayoutRounding = true,
            IsHitTestVisible = false
        };
        var border = brush("BorderSubtleBrush");
        var foreground = brush("PrimaryTextBrush");
        for (var column = 0; column < row.Cells.Length; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = fontSize,
                FontFamily = BodyFont,
                FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = foreground,
                TextAlignment = row.Alignment[column] switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                }
            };

            foreach (var part in row.Cells[column])
            {
                if (part.IsBreak)
                {
                    text.Inlines.Add(new LineBreak());
                    continue;
                }

                var run = new Run(part.Text);
                if (part.Style.HasFlag(InlineStyle.Bold))
                    run.FontWeight = FontWeights.Bold;
                if (part.Style.HasFlag(InlineStyle.Italic))
                    run.FontStyle = FontStyles.Italic;
                if (part.Style.HasFlag(InlineStyle.Strike))
                    run.TextDecorations = TextDecorations.Strikethrough;
                if (part.Style.HasFlag(InlineStyle.Code))
                {
                    run.FontFamily = CodeFont;
                    run.Background = brush("MarkdownCodeBrush");
                }
                if (part.Style.HasFlag(InlineStyle.Link))
                {
                    run.Foreground = brush("AccentBrush");
                    run.TextDecorations = TextDecorations.Underline;
                }
                text.Inlines.Add(run);
            }

            var cell = new Border
            {
                BorderBrush = border,
                BorderThickness = new Thickness(column == 0 ? 1 : 0, row.IsHeader ? 1 : 0, 1, 1),
                Padding = new Thickness(8, 5, 8, 5),
                Background = row.IsHeader ? brush("MarkdownTableBrush") : null,
                Child = text
            };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        return grid;
    }
}
