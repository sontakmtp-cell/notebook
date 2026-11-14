using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PromptNotebook.Helpers;

/// <summary>
/// Attached properties cho FlowDocumentScrollViewer để render Markdown thuần WPF theo chuẩn MVVM.
/// </summary>
public static class MarkdownBehavior
{
    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.RegisterAttached(
            "MarkdownText",
            typeof(string),
            typeof(MarkdownBehavior),
            new PropertyMetadata(string.Empty, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty BaseFontSizeProperty =
        DependencyProperty.RegisterAttached(
            "BaseFontSize",
            typeof(double),
            typeof(MarkdownBehavior),
            new PropertyMetadata(14.0, OnMarkdownPropertyChanged));

    public static string GetMarkdownText(DependencyObject obj) => (string)obj.GetValue(MarkdownTextProperty);
    public static void SetMarkdownText(DependencyObject obj, string value) => obj.SetValue(MarkdownTextProperty, value);

    public static double GetBaseFontSize(DependencyObject obj) => (double)obj.GetValue(BaseFontSizeProperty);
    public static void SetBaseFontSize(DependencyObject obj, double value) => obj.SetValue(BaseFontSizeProperty, value);

    private static void OnMarkdownPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            var text = GetMarkdownText(viewer) ?? string.Empty;
            var fontSize = GetBaseFontSize(viewer);
            viewer.Document = MarkdownRenderer.Render(text, fontSize > 0 ? fontSize : 14.0);
        }
    }
}

/// <summary>
/// Bộ render Markdown sang WPF FlowDocument không cần thư viện ngoài.
/// Tự động đồng bộ màu sắc theo Dark/Light Theme thông qua DynamicResource.
/// Hỗ trợ: Headings (#..####), Code block (```), Inline code (`), Bold/Italic/Strike,
/// Blockquote (>), Danh sách (-/*/1.), Bảng (|...|), Đường kẻ ngang (---), Liên kết ([text](url)).
/// </summary>
public static partial class MarkdownRenderer
{
    private static readonly FontFamily DefaultFont = new("Segoe UI");
    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Mono, Courier New");

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\*\s*\*\s*\*|-\s*-\s*-|_\s*_\s*_)\s*$")]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"^\s*([-*+])\s+(.+)$")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^\s*(\d+)[.)]\s+(.+)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^\|(.+)\|\s*$")]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"^\|[\s:\-\|]+\|\s*$")]
    private static partial Regex TableSeparatorRegex();

    public static FlowDocument Render(string markdown, double baseFontSize = 14.0)
    {
        var doc = new FlowDocument
        {
            FontFamily = DefaultFont,
            FontSize = baseFontSize,
            PagePadding = new Thickness(14, 12, 14, 16),
            TextAlignment = TextAlignment.Left
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "PrimaryTextBrush");

        if (string.IsNullOrWhiteSpace(markdown))
        {
            var emptyPara = new Paragraph(new Run("Chưa có nội dung để xem trước."))
            {
                FontStyle = FontStyles.Italic
            };
            emptyPara.SetResourceReference(TextElement.ForegroundProperty, "SecondaryTextBrush");
            doc.Blocks.Add(emptyPara);
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // 1. Dòng trống
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // 2. Fenced Code Block (```lang ... ```)
            var trimmedStart = line.TrimStart();
            if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
            {
                var lang = trimmedStart[3..].Trim();
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++; // bỏ qua dòng đóng ```

                doc.Blocks.Add(CreateCodeBlock(string.Join("\n", codeLines), lang, baseFontSize));
                continue;
            }

            // 3. Đường kẻ ngang (---, ***, ___)
            if (HrRegex().IsMatch(line.Trim()))
            {
                doc.Blocks.Add(CreateHorizontalRule());
                i++;
                continue;
            }

            // 4. Heading (# -> ######)
            var headingMatch = HeadingRegex().Match(trimmedStart);
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var headingText = headingMatch.Groups[2].Value.Trim();
                doc.Blocks.Add(CreateHeading(headingText, level, baseFontSize));
                i++;
                continue;
            }

            // 5. Blockquote (> ...)
            if (trimmedStart.StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var q = lines[i].TrimStart()[1..];
                    if (q.StartsWith(' ')) q = q[1..];
                    quoteLines.Add(q);
                    i++;
                }
                doc.Blocks.Add(CreateBlockquote(string.Join("\n", quoteLines), baseFontSize));
                continue;
            }

            // 6. Bảng Markdown (| col1 | col2 |)
            if (TableRowRegex().IsMatch(line.Trim()) &&
                i + 1 < lines.Length &&
                TableSeparatorRegex().IsMatch(lines[i + 1].Trim()))
            {
                var headerCells = ParseTableCells(line.Trim());
                i += 2; // bỏ qua header và separator
                var bodyRows = new List<List<string>>();
                while (i < lines.Length && TableRowRegex().IsMatch(lines[i].Trim()))
                {
                    bodyRows.Add(ParseTableCells(lines[i].Trim()));
                    i++;
                }
                doc.Blocks.Add(CreateTable(headerCells, bodyRows, baseFontSize));
                continue;
            }

            // 7. Danh sách không thứ tự (-, *, +)
            if (UnorderedListRegex().IsMatch(line))
            {
                var list = new List
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 2, 0, 8),
                    Padding = new Thickness(22, 0, 0, 0)
                };
                while (i < lines.Length)
                {
                    var m = UnorderedListRegex().Match(lines[i]);
                    if (!m.Success) break;
                    var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2), LineHeight = baseFontSize * 1.45 };
                    AddInlines(p.Inlines, m.Groups[2].Value, baseFontSize);
                    list.ListItems.Add(new ListItem(p));
                    i++;
                }
                doc.Blocks.Add(list);
                continue;
            }

            // 8. Danh sách có thứ tự (1., 2., ...)
            if (OrderedListRegex().IsMatch(line))
            {
                var list = new List
                {
                    MarkerStyle = TextMarkerStyle.Decimal,
                    Margin = new Thickness(0, 2, 0, 8),
                    Padding = new Thickness(24, 0, 0, 0)
                };
                while (i < lines.Length)
                {
                    var m = OrderedListRegex().Match(lines[i]);
                    if (!m.Success) break;
                    var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2), LineHeight = baseFontSize * 1.45 };
                    AddInlines(p.Inlines, m.Groups[2].Value, baseFontSize);
                    list.ListItems.Add(new ListItem(p));
                    i++;
                }
                doc.Blocks.Add(list);
                continue;
            }

            // 9. Đoạn văn bản thường (gộp các dòng liên tiếp không thuộc block khác)
            var paraLines = new List<string>();
            while (i < lines.Length && !IsBlockStart(lines, i))
            {
                paraLines.Add(lines[i]);
                i++;
            }

            var para = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 8),
                LineHeight = baseFontSize * 1.5
            };
            AddInlines(para.Inlines, string.Join("\n", paraLines), baseFontSize);
            doc.Blocks.Add(para);
        }

        return doc;
    }

    private static bool IsBlockStart(string[] lines, int index)
    {
        var line = lines[index];
        if (string.IsNullOrWhiteSpace(line)) return true;

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal)) return true;
        if (trimmed.StartsWith('>')) return true;
        if (HeadingRegex().IsMatch(trimmed)) return true;
        if (HrRegex().IsMatch(line.Trim())) return true;
        if (UnorderedListRegex().IsMatch(line)) return true;
        if (OrderedListRegex().IsMatch(line)) return true;
        if (TableRowRegex().IsMatch(line.Trim()) &&
            index + 1 < lines.Length &&
            TableSeparatorRegex().IsMatch(lines[index + 1].Trim()))
            return true;

        return false;
    }

    private static Block CreateHeading(string text, int level, double baseFontSize)
    {
        var scale = level switch
        {
            1 => 1.65,
            2 => 1.4,
            3 => 1.22,
            4 => 1.1,
            _ => 1.02
        };

        var para = new Paragraph
        {
            FontSize = Math.Round(baseFontSize * scale),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, level == 1 ? 6 : 12, 0, 6),
            Padding = level <= 2 ? new Thickness(0, 0, 0, 4) : new Thickness(0),
            BorderThickness = level <= 2 ? new Thickness(0, 0, 0, 1) : new Thickness(0)
        };
        para.SetResourceReference(TextElement.ForegroundProperty, "PrimaryTextBrush");
        if (level <= 2)
        {
            para.SetResourceReference(Block.BorderBrushProperty, "BorderSubtleBrush");
        }

        AddInlines(para.Inlines, text, para.FontSize);
        return para;
    }

    private static Block CreateCodeBlock(string code, string lang, double baseFontSize)
    {
        var section = new Section
        {
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(12, 10, 12, 10),
            BorderThickness = new Thickness(1)
        };
        section.SetResourceReference(TextElement.BackgroundProperty, "SidebarBackgroundBrush");
        section.SetResourceReference(Block.BorderBrushProperty, "BorderSubtleBrush");

        if (!string.IsNullOrEmpty(lang))
        {
            var langPara = new Paragraph(new Run(lang.ToUpperInvariant()))
            {
                FontFamily = DefaultFont,
                FontSize = Math.Max(10, baseFontSize - 3),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            langPara.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            section.Blocks.Add(langPara);
        }

        var codePara = new Paragraph(new Run(code))
        {
            FontFamily = MonoFont,
            FontSize = Math.Max(11, baseFontSize - 1),
            Margin = new Thickness(0),
            LineHeight = baseFontSize * 1.4
        };
        codePara.SetResourceReference(TextElement.ForegroundProperty, "PrimaryTextBrush");
        section.Blocks.Add(codePara);

        return section;
    }

    private static Block CreateBlockquote(string text, double baseFontSize)
    {
        var section = new Section
        {
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(12, 6, 10, 6),
            BorderThickness = new Thickness(3, 0, 0, 0)
        };
        section.SetResourceReference(Block.BorderBrushProperty, "AccentBrush");
        section.SetResourceReference(TextElement.BackgroundProperty, "SidebarBackgroundBrush");

        var para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = baseFontSize * 1.45
        };
        para.SetResourceReference(TextElement.ForegroundProperty, "SecondaryTextBrush");
        AddInlines(para.Inlines, text, baseFontSize);
        section.Blocks.Add(para);

        return section;
    }

    private static Block CreateHorizontalRule()
    {
        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 10),
            SnapsToDevicePixels = true
        };
        line.SetResourceReference(Border.BackgroundProperty, "BorderSubtleBrush");
        return new BlockUIContainer(line);
    }

    private static List<string> ParseTableCells(string rowLine)
    {
        var inner = rowLine.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|')) inner = inner[..^1];
        return inner.Split('|').Select(c => c.Trim()).ToList();
    }

    private static Block CreateTable(List<string> headers, List<List<string>> rows, double baseFontSize)
    {
        var colCount = Math.Max(headers.Count, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 4, 0, 10),
            BorderThickness = new Thickness(1)
        };
        table.SetResourceReference(Table.BorderBrushProperty, "BorderSubtleBrush");

        for (int c = 0; c < colCount; c++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();

        // Header row
        var headerRow = new TableRow();
        headerRow.SetResourceReference(TableRow.BackgroundProperty, "SidebarBackgroundBrush");
        for (int c = 0; c < colCount; c++)
        {
            var cellText = c < headers.Count ? headers[c] : string.Empty;
            var p = new Paragraph { Margin = new Thickness(0), FontWeight = FontWeights.SemiBold };
            AddInlines(p.Inlines, cellText, baseFontSize);
            var cell = new TableCell(p)
            {
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(0, 0, c < colCount - 1 ? 1 : 0, 1)
            };
            cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderSubtleBrush");
            headerRow.Cells.Add(cell);
        }
        group.Rows.Add(headerRow);

        // Data rows
        for (int r = 0; r < rows.Count; r++)
        {
            var dataRow = new TableRow();
            var isLastRow = r == rows.Count - 1;
            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < rows[r].Count ? rows[r][c] : string.Empty;
                var p = new Paragraph { Margin = new Thickness(0) };
                AddInlines(p.Inlines, cellText, baseFontSize);
                var cell = new TableCell(p)
                {
                    Padding = new Thickness(8, 5, 8, 5),
                    BorderThickness = new Thickness(0, 0, c < colCount - 1 ? 1 : 0, isLastRow ? 0 : 1)
                };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderSubtleBrush");
                dataRow.Cells.Add(cell);
            }
            group.Rows.Add(dataRow);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static void AddInlines(InlineCollection target, string text, double baseFontSize)
    {
        if (string.IsNullOrEmpty(text))
            return;

        int i = 0;
        var buffer = new System.Text.StringBuilder();

        void FlushBuffer()
        {
            if (buffer.Length > 0)
            {
                target.Add(new Run(buffer.ToString()));
                buffer.Clear();
            }
        }

        while (i < text.Length)
        {
            // Ký tự escape (\*, \`, \\)
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                buffer.Append(text[i + 1]);
                i += 2;
                continue;
            }

            // Inline code: `code`
            if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    FlushBuffer();
                    var codeText = text[(i + 1)..close];
                    var codeSpan = new Span(new Run($" {codeText} "))
                    {
                        FontFamily = MonoFont,
                        FontSize = Math.Max(11, baseFontSize - 1)
                    };
                    codeSpan.SetResourceReference(TextElement.BackgroundProperty, "BadgeBackgroundBrush");
                    codeSpan.SetResourceReference(TextElement.ForegroundProperty, "PrimaryTextBrush");
                    target.Add(codeSpan);
                    i = close + 1;
                    continue;
                }
            }

            // Link: [label](url)
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 2)
                    {
                        var label = text[(i + 1)..closeBracket];
                        var url = text[(closeBracket + 2)..closeParen].Trim();
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            FlushBuffer();
                            var link = new Hyperlink { NavigateUri = uri };
                            link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                            AddInlines(link.Inlines, label, baseFontSize);
                            link.RequestNavigate += (_, e) =>
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                                }
                                catch
                                {
                                    // Bỏ qua nếu không mở được trình duyệt
                                }
                                e.Handled = true;
                            };
                            target.Add(link);
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }
            }

            // Bold + Italic: ***text***
            if (i + 2 < text.Length && text.AsSpan(i, 3).SequenceEqual("***"))
            {
                var close = text.IndexOf("***", i + 3, StringComparison.Ordinal);
                if (close > i + 3)
                {
                    FlushBuffer();
                    var bold = new Bold();
                    var italic = new Italic();
                    AddInlines(italic.Inlines, text[(i + 3)..close], baseFontSize);
                    bold.Inlines.Add(italic);
                    target.Add(bold);
                    i = close + 3;
                    continue;
                }
            }

            // Bold: **text** hoặc __text__
            if (i + 1 < text.Length && (text.AsSpan(i, 2).SequenceEqual("**") || text.AsSpan(i, 2).SequenceEqual("__")))
            {
                var marker = text.Substring(i, 2);
                var close = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    FlushBuffer();
                    var bold = new Bold();
                    AddInlines(bold.Inlines, text[(i + 2)..close], baseFontSize);
                    target.Add(bold);
                    i = close + 2;
                    continue;
                }
            }

            // Strikethrough: ~~text~~
            if (i + 1 < text.Length && text.AsSpan(i, 2).SequenceEqual("~~"))
            {
                var close = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    FlushBuffer();
                    var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
                    strike.SetResourceReference(TextElement.ForegroundProperty, "SecondaryTextBrush");
                    AddInlines(strike.Inlines, text[(i + 2)..close], baseFontSize);
                    target.Add(strike);
                    i = close + 2;
                    continue;
                }
            }

            // Italic: *text* hoặc _text_ (tránh làm hỏng snake_case)
            if (text[i] == '*' || text[i] == '_')
            {
                var ch = text[i];
                var validStart = ch == '*' || i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                if (validStart)
                {
                    var close = text.IndexOf(ch, i + 1);
                    if (close > i + 1)
                    {
                        var validEnd = ch == '*' || close + 1 >= text.Length || !char.IsLetterOrDigit(text[close + 1]);
                        if (validEnd)
                        {
                            FlushBuffer();
                            var italic = new Italic();
                            AddInlines(italic.Inlines, text[(i + 1)..close], baseFontSize);
                            target.Add(italic);
                            i = close + 1;
                            continue;
                        }
                    }
                }
            }

            buffer.Append(text[i]);
            i++;
        }

        FlushBuffer();
    }
}
