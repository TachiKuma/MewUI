using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class DocumentHighlighterTests
{
    private static IHighlightingDefinition CSharp()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        return definition;
    }

    /// <summary>
    /// Rules are tried in registration order and the first one to match a position keeps it, so a
    /// JSON key stays a key even though the plain string rule registered after it matches the same
    /// text. Only one scan path may exist, or the two disagree on exactly this.
    /// </summary>
    [TestMethod]
    public void EarlierRulesWinWhereTheyOverlapLaterOnes()
    {
        var definition = HighlightingManager.Instance.GetDefinition("JSON");
        Assert.IsNotNull(definition);
        Assert.IsEmpty(definition.MainRuleSet.Spans, "This guards the definition shape the case relies on.");
        var document = new TextDocument("""{ "name": "value" }""");
        using var highlighter = new DocumentHighlighter(document, definition);

        var line = highlighter.HighlightLine(1);

        var key = line.Sections.Single(section => section.Offset == 2);
        var value = line.Sections.Single(section => section.Offset == 10);
        Assert.AreNotEqual(key.Color.Foreground, value.Color.Foreground);
    }

    [TestMethod]
    public void BlockCommentKeepsItsColorAcrossLines()
    {
        var document = new TextDocument("int a; /* open\nstill comment\nend */ int b;");
        using var highlighter = new DocumentHighlighter(document, CSharp());

        var middle = highlighter.HighlightLine(2);

        var section = middle.Sections.Single();
        Assert.AreEqual(0, section.Offset);
        Assert.AreEqual("still comment".Length, section.Length);
        Assert.AreEqual(Color.FromRgb(106, 153, 85), section.Color.Foreground);
    }

    [TestMethod]
    public void TextAfterTheBlockCommentClosesIsHighlightedNormally()
    {
        var document = new TextDocument("/* comment\n*/ int value;");
        using var highlighter = new DocumentHighlighter(document, CSharp());

        var second = highlighter.HighlightLine(2);

        Assert.IsTrue(second.Sections.Any(section =>
            section.Color.Foreground == Color.FromRgb(78, 201, 176) && section.Length == 3),
            "The type keyword after the closing delimiter should be colored by the main rule set.");
    }

    [TestMethod]
    public void VerbatimStringSpansLines()
    {
        var document = new TextDocument("var path = @\"C:\\one\nstill string\";");
        using var highlighter = new DocumentHighlighter(document, CSharp());

        var second = highlighter.HighlightLine(2);

        Assert.IsTrue(second.Sections.Any(section => section.Offset == 0),
            "The continuation line of a verbatim string must be colored.");
    }

    [TestMethod]
    public void ClosingACommentInvalidatesTheFollowingLines()
    {
        var document = new TextDocument("/* open\nbody\ntail");
        using var highlighter = new DocumentHighlighter(document, CSharp());
        int changedFrom = 0;
        highlighter.HighlightingStateChanged += (from, _) => changedFrom = from;
        highlighter.HighlightLine(3);

        // Close the comment on line 1; lines below must be rescanned.
        document.Insert(7, " */");
        highlighter.HighlightLine(1);

        Assert.AreEqual(2, changedFrom);
    }

    [TestMethod]
    public void DefinitionsWithoutSpansStillHighlight()
    {
        var document = new TextDocument("public int value = 3;");
        using var highlighter = new DocumentHighlighter(document, CSharp());

        var line = highlighter.HighlightLine(1);

        Assert.IsTrue(line.Sections.Any(section => section.Offset == 0 && section.Length == 6));
    }
}
