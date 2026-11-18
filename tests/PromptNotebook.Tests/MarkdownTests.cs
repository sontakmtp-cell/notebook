using PromptNotebook.Markdown;

internal static class MarkdownTests
{
    public static void Run()
    {
        var source = string.Join("\r\n",
            "# Tiếng Việt **đậm**, *nghiêng*, ~~gạch~~ và `code` ##",
            "###### Mức sáu",
            "",
            "> Trích dẫn",
            ">> Lồng",
            "",
            "- Một",
            "  - Hai",
            "",
            "3. Ba",
            "3. Bốn",
            "",
            "[**Liên kết**](https://example.org) ![Ảnh mẫu](ảnh.png)",
            "",
            "---",
            "",
            "```json",
            """{"ok": true}""",
            "```   ",
            "",
            "| Cột A | Cột B |",
            "| ----- | ----- |",
            "| x     | 中文  |",
            "",
            "Setext",
            "---",
            "",
            "> ```cs",
            "> string tên = \"Chữ\";",
            "> ```",
            "");
        var layout = MarkdownLayout.Parse(source);
        var first = layout.GetLine(1)!;
        Assert(first.Heading == 1 && first.Projections.First().Length == 2, "H1 marker");
        Assert(first.Styles.Select(s => s.Style).SequenceEqual(
            new[] { InlineStyle.Bold, InlineStyle.Italic, InlineStyle.Strike, InlineStyle.Code }), "Inline formatting");
        Assert(first.Projections.Count == 10, "Opening and closing syntax is projected");
        Assert(layout.GetLine(2)?.Heading == 6, "H6 support");
        Assert(layout.GetLine(5)?.Projections.Count(p => p.Text == "│ ") == 2, "Nested blockquotes");
        Assert(layout.GetLine(7)?.Projections.Single().Text == "• " && layout.GetLine(8)?.Projections.Single().Text == "◦ ",
            "Nested unordered lists");
        Assert(layout.GetLine(10)?.Projections.Single().Text == "3. " && layout.GetLine(11)?.Projections.Single().Text == "4. ",
            "Sequential numbered display");
        var links = layout.GetLine(13)!;
        Assert(links.Styles.Any(s => s.Style == InlineStyle.Link) && links.Styles.Any(s => s.Style == InlineStyle.Bold),
            "Formatted link label");
        Assert(links.Projections.Single(p => p.Kind == ProjectionKind.Image).Target == "ảnh.png", "Local image reference");
        Assert(layout.GetLine(15)?.IsRule == true, "Horizontal rule");
        Assert(layout.GetLine(17)?.Projections.Single().Kind == ProjectionKind.CopyCode, "Code copy affordance");
        Assert(layout.GetLine(18) is { CodeLine: 1, CodeStart: 0, Fence.Code: """{"ok": true}""" }, "Fence source mapping");
        Assert(layout.GetLine(19)?.Projections.Single().Length == 6, "Closing fence with trailing whitespace");
        Assert(layout.GetLine(21) is { IsTableHeader: true } header && header.Projections.Count == 3, "Styled table header");
        Assert(layout.GetLine(22)?.IsTableSeparator == true && layout.GetLine(23)?.IsTable == true, "Table separator and body");
        Assert(layout.GetLine(25)?.Heading == 2 && layout.GetLine(26)?.Projections.Single().Kind == ProjectionKind.Hidden,
            $"Setext heading (heading={layout.GetLine(25)?.Heading}, separator={layout.GetLine(26)?.Heading}, rule={layout.GetLine(26)?.IsRule})");
        Assert(layout.GetLine(29) is { CodeStart: 2, Fence.Code: "string tên = \"Chữ\";" }, "Nested fence source and copy text");

        foreach (var specimen in new[] { source, "#", ">\r", "`x\ny`", "***both***", "````\r\n\r\n    ",
            "````\n```not a closer\n\n```\n````", "a | b\n--- | ---\nc | d", "|a|b|\n|-|-|\n| | |",
            "[reference]: https://example.org\n\n[A][reference]", "- > quote\n  > `code`", "[a\nb](test)" })
        {
            var parsed = MarkdownLayout.Parse(specimen);
            var lines = specimen.Replace("\r\n", "\n").Split('\n');
            for (var i = 1; i <= lines.Length; i++)
            {
                if (parsed.GetLine(i) is not { } line)
                    continue;
                var end = 0;
                foreach (var projection in line.Projections)
                {
                    Assert(projection.Start >= end && projection.End <= line.Source.Length, "Nonoverlapping source projections");
                    end = projection.End;
                }
                Assert(line.Styles.All(style => style.Start >= 0 && style.Start + style.Length <= line.Source.Length),
                    "Styles map onto original source");
            }
        }
        var random = new Random(41);
        string[] fragments = ["# Title", "  - nested", "  > > quote", "| a | \\| |", "| - | - |",
            "```js", "````", "~~~", "[a](./b.txt)", "![](./x.png)", "***", "   ", "text 😀", ""];
        for (var i = 0; i < 1_000; i++)
        {
            var specimen = string.Join(i % 2 == 0 ? "\r\n" : "\n",
                Enumerable.Range(0, random.Next(1, 25)).Select(_ => fragments[random.Next(fragments.Length)]));
            _ = MarkdownLayout.Parse(specimen);
        }
        Console.WriteLine("[PASS] Markdown AST spans, heading and inline styles, quotes, lists, links, images, rules, fences and tables");
    }

    private static void Assert(bool test, string label)
    {
        if (!test)
            throw new InvalidOperationException(label);
    }
}
