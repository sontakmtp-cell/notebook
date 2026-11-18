using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace PromptNotebook.Helpers;

public static class JsonHighlighting
{
    private static readonly Lazy<IHighlightingDefinition> Dark = new(() => Load(true));
    private static readonly Lazy<IHighlightingDefinition> Light = new(() => Load(false));

    public static IHighlightingDefinition ForTheme(bool dark) => dark ? Dark.Value : Light.Value;

    private static IHighlightingDefinition Load(bool dark)
    {
        var key = dark ? "#9FBFFF" : "#2E529A";
        var text = dark ? "#A7DCB4" : "#216740";
        var number = dark ? "#F4BE8C" : "#984717";
        var literal = dark ? "#D1ADFF" : "#703C9C";

        using var reader = XmlReader.Create(new StringReader($$"""
            <SyntaxDefinition name="JSON" extensions=".json" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Key" foreground="{{key}}" />
              <Color name="String" foreground="{{text}}" />
              <Color name="Number" foreground="{{number}}" />
              <Color name="Literal" foreground="{{literal}}" />
              <RuleSet>
                <Rule color="Key">&quot;(?:\\.|[^&quot;\\])*&quot;(?=\s*:)</Rule>
                <Span color="String" begin="&quot;" end="&quot;">
                  <RuleSet><Span begin="\\" end="." /></RuleSet>
                </Span>
                <Rule color="Number">-?\b[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\b</Rule>
                <Keywords color="Literal"><Word>true</Word><Word>false</Word><Word>null</Word></Keywords>
              </RuleSet>
            </SyntaxDefinition>
            """));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
