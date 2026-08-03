using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class HighlightingTests
{
    [TestMethod]
    public void BuiltInCSharpDefinitionClassifiesKeywordsAndStrings()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);

        var elements = HighlightingTestHost.Colorize(definition, "public string Value = \"text\";");

        Assert.IsTrue(elements.Any(element => element.RelativeTextOffset == 0 && element.DocumentLength == 6));
        Assert.IsTrue(elements.Any(element => element.RelativeTextOffset == 7 && element.DocumentLength == 6));
        Assert.IsTrue(elements.Any(element => element.DocumentLength == 6 && element.RelativeTextOffset > 7));
    }
}

/// <summary>Runs a colorizer over a single-line document through the transformer contract.</summary>
internal static class HighlightingTestHost
{
    public static List<VisualLineElement> Colorize(
        IHighlightingDefinition definition,
        string text,
        bool isDarkTheme = true)
    {
        var document = new TextDocument(text);
        var elements = new List<VisualLineElement>();
        new HighlightingColorizer(definition, () => isDarkTheme)
            .Transform(new SingleLineContext(document), elements);
        return elements;
    }

    private sealed class SingleLineContext(TextDocument document) : ITextRunConstructionContext
    {
        public TextDocument Document => document;
        public DocumentLine CurrentDocumentLine => document.GetLineByNumber(1);
        public TextRunStyle DefaultStyle => TextRunStyle.Default;
    }
}
