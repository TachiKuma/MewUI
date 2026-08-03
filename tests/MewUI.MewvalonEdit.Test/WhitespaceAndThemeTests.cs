using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class WhitespaceAndThemeTests
{
    [TestMethod]
    public void SpaceMarkersArePaintedInTheMarkerColor()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = true;
        var pipeline = editor.TextArea.TextView.Extensions;
        var logical = new LogicalTextLine(0, 0, 3, 3);

        var projected = pipeline.Projections[0].Project(
            new TextProjectionContext(logical, editor.Text.AsMemory()));
        Assert.AreEqual("a·b", projected.Text.ToString());

        var spans = new List<TextPaintSpan>();
        foreach (var classifier in pipeline.Classifiers)
        {
            classifier.Classify(
                new TextClassificationContext(logical, projected.Text, projected.OffsetMap), spans);
        }

        Assert.ContainsSingle(spans);
        Assert.AreEqual(new TextRange(1, 1), spans[0].Range);
        Assert.AreNotEqual(editor.Foreground, spans[0].Foreground);
    }

    [TestMethod]
    public void SpaceMarkersAreNotPaintedWhenHidden()
    {
        var editor = new TextEditor { Text = "a b" };
        var spans = new List<TextPaintSpan>();
        var logical = new LogicalTextLine(0, 0, 3, 3);

        foreach (var classifier in editor.TextArea.TextView.Extensions.Classifiers)
        {
            classifier.Classify(
                new TextClassificationContext(logical, editor.Text.AsMemory(), IdentityTextOffsetMap.Instance),
                spans);
        }

        Assert.IsEmpty(spans);
    }

    [TestMethod]
    public void HighlightingPicksThemeVariantColors()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        const string SOURCE = "public int Value = 3;";

        var dark = HighlightingTestHost.Colorize(definition, SOURCE, isDarkTheme: true);
        var light = HighlightingTestHost.Colorize(definition, SOURCE, isDarkTheme: false);

        var darkKeyword = dark.First(element => element.RelativeTextOffset == 0);
        var lightKeyword = light.First(element => element.RelativeTextOffset == 0);
        Assert.AreEqual(Color.FromRgb(86, 156, 214), darkKeyword.TextRunProperties.ForegroundBrush);
        Assert.AreEqual(Color.FromRgb(0, 0, 255), lightKeyword.TextRunProperties.ForegroundBrush);
    }

    [TestMethod]
    public void HighlightingDefaultsToDarkWhenNoThemeSourceIsGiven()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var document = new TextDocument("public");
        var elements = new List<VisualLineElement>();

        new HighlightingColorizer(definition).Transform(new DefaultThemeContext(document), elements);

        Assert.AreEqual(Color.FromRgb(86, 156, 214), elements[0].TextRunProperties.ForegroundBrush);
    }

    private sealed class DefaultThemeContext(TextDocument document) : ITextRunConstructionContext
    {
        public TextDocument Document => document;
        public DocumentLine CurrentDocumentLine => document.GetLineByNumber(1);
        public TextRunStyle DefaultStyle => TextRunStyle.Default;
    }
}
