using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class HighlightingLoaderTests
{
    private const string SAMPLE = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="Mini" extensions=".mini" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="Green" />
          <Color name="String" foreground="#A31515" />
          <RuleSet>
            <Span color="Comment" multiline="true">
              <Begin>/\*</Begin>
              <End>\*/</End>
            </Span>
            <Span color="Comment">
              <Begin>//</Begin>
            </Span>
            <Keywords fontWeight="bold" foreground="Blue">
              <Word>if</Word>
              <Word>else</Word>
            </Keywords>
            <Rule color="String">"[^"]*"</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    [TestMethod]
    public void LoadsNameColorsRulesAndSpans()
    {
        var definition = HighlightingLoader.Load(SAMPLE);

        Assert.AreEqual("Mini", definition.Name);
        Assert.AreEqual(Color.FromRgb(0, 128, 0), definition.GetNamedColor("Comment")?.Foreground);
        Assert.AreEqual(Color.FromHex("#A31515"), definition.GetNamedColor("String")?.Foreground);
        Assert.ContainsSingle(definition.MainRuleSet.Spans);
        Assert.IsGreaterThanOrEqualTo(3, definition.MainRuleSet.Rules.Count);
    }

    [TestMethod]
    public void LoadedSpansCarryAcrossLines()
    {
        var definition = HighlightingLoader.Load(SAMPLE);
        var document = new TextDocument("/* start\ninside\n*/ if");
        using var highlighter = new DocumentHighlighter(document, definition);

        var second = highlighter.HighlightLine(2);

        var section = second.Sections.Single();
        Assert.AreEqual(0, section.Offset);
        Assert.AreEqual("inside".Length, section.Length);
        Assert.AreEqual(Color.FromRgb(0, 128, 0), section.Color.Foreground);
    }

    [TestMethod]
    public void KeywordListsBecomeWordBoundedRules()
    {
        var definition = HighlightingLoader.Load(SAMPLE);
        var document = new TextDocument("if x");
        using var highlighter = new DocumentHighlighter(document, definition);

        var line = highlighter.HighlightLine(1);

        var keyword = line.Sections.Single(section => section.Offset == 0);
        Assert.AreEqual(2, keyword.Length);
        Assert.AreEqual(FontWeight.Bold, keyword.Color.FontWeight);
    }

    /// <summary>
    /// The definitions AvalonEdit ships write element-body patterns free-form with '#' comments,
    /// which only parse under IgnorePatternWhitespace.
    /// </summary>
    [TestMethod]
    public void ElementBodyPatternsAllowWhitespaceAndComments()
    {
        const string SOURCE = """
            <?xml version="1.0"?>
            <SyntaxDefinition name="Spaced" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Ident" foreground="Blue" />
              <RuleSet>
                <Rule color="Ident">
                  \b
                  [\d\w_]+  # an identifier
                  (?=\s*\()  # followed by an opening parenthesis
                </Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;

        var definition = HighlightingLoader.Load(SOURCE);
        var document = new TextDocument("value Compute()");
        using var highlighter = new DocumentHighlighter(document, definition);

        var line = highlighter.HighlightLine(1);

        var call = line.Sections.Single();
        Assert.AreEqual(6, call.Offset);
        Assert.AreEqual("Compute".Length, call.Length);
    }

    [TestMethod]
    public void InvalidXmlIsReportedAsADefinitionError()
        => Assert.ThrowsExactly<HighlightingDefinitionInvalidException>(
            () => HighlightingLoader.Load("<SyntaxDefinition"));

    [TestMethod]
    public void RegisteredDefinitionsResolveByExtension()
    {
        var definition = HighlightingLoader.Load(SAMPLE);
        HighlightingManager.Instance.RegisterHighlighting("Mini", [".mini"], definition);

        Assert.AreSame(definition, HighlightingManager.Instance.GetDefinitionByExtension(".mini"));
    }
}
