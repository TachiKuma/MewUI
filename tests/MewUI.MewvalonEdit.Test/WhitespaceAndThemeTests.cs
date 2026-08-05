using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
[DoNotParallelize]
public sealed class WhitespaceAndThemeTests
{
    [TestMethod]
    public void SpaceMarkersArePaintedInTheMarkerColor()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = true;
        var pipeline = editor.TextArea.TextView.Extensions;
        var logical = new LogicalTextLine(0, 0, 3, 3);

        // Run every projection in registration order, as the engine does; the element generator
        // projection sits ahead of the space markers and passes text without generators through.
        var projected = new ProjectedText(editor.Text.AsMemory(), IdentityTextOffsetMap.Instance);
        foreach (var projection in pipeline.Projections)
        {
            projected = projection.Project(new TextProjectionContext(logical, projected.Text));
        }
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
    public void OneColorizerFollowsAThemeSwitchWithoutRebuilding()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var colorizer = new HighlightingColorizer(definition);

        // The same instance across the switch: the colorizer reads the theme per line, so nothing
        // has to be rebuilt when it changes.
        var dark = HighlightingTestHost.Colorize(colorizer, "public", isDarkTheme: true);
        var light = HighlightingTestHost.Colorize(colorizer, "public", isDarkTheme: false);

        Assert.AreEqual(Color.FromRgb(86, 156, 214), dark[0].TextRunProperties.ForegroundBrush);
        Assert.AreEqual(Color.FromRgb(0, 0, 255), light[0].TextRunProperties.ForegroundBrush);
    }
}
