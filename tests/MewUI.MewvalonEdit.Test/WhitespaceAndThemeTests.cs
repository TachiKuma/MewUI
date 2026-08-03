using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
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
        var text = "public int Value = 3;".AsMemory();
        var context = new TextClassificationContext(
            new LogicalTextLine(0, 0, text.Length, text.Length), text, IdentityTextOffsetMap.Instance);

        var dark = new List<TextPaintSpan>();
        new HighlightingColorizer(definition, () => true).Classify(in context, dark);
        var light = new List<TextPaintSpan>();
        new HighlightingColorizer(definition, () => false).Classify(in context, light);

        var darkKeyword = dark.First(span => span.Range.Start == 0);
        var lightKeyword = light.First(span => span.Range.Start == 0);
        Assert.AreEqual(Color.FromRgb(86, 156, 214), darkKeyword.Foreground);
        Assert.AreEqual(Color.FromRgb(0, 0, 255), lightKeyword.Foreground);
    }

    [TestMethod]
    public void HighlightingDefaultsToDarkWhenNoThemeSourceIsGiven()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var text = "public".AsMemory();
        var spans = new List<TextPaintSpan>();

        new HighlightingColorizer(definition).Classify(
            new TextClassificationContext(
                new LogicalTextLine(0, 0, text.Length, text.Length), text, IdentityTextOffsetMap.Instance),
            spans);

        Assert.AreEqual(Color.FromRgb(86, 156, 214), spans[0].Foreground);
    }
}
