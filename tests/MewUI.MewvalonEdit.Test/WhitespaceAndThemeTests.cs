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
    /// <summary>
    /// A space is stood in for by a marker glyph in its own colour, so it reads as whitespace rather
    /// than as content. The original builds it as a single-character element too.
    /// </summary>
    [TestMethod]
    public void SpaceMarkersArePaintedInTheMarkerColor()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = true;

        var element = ConstructSingleCharacterElement(editor, offset: 1);

        Assert.IsNotNull(element, "No element stood in for the space.");
        Assert.AreEqual(editor.WhitespaceMarkerColor, element.Foreground);
        Assert.AreNotEqual(editor.Foreground, element.Foreground);
    }

    [TestMethod]
    public void SpaceMarkersAreNotPaintedWhenHidden()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = false;

        Assert.IsNull(ConstructSingleCharacterElement(editor, offset: 1));
    }

    /// <summary>
    /// A control character is stood in for by a box naming it. A tab is a control character too, and
    /// must not be boxed: the original settles it before the box is reached.
    /// </summary>
    [TestMethod]
    public void ControlCharactersAreBoxedButTabsAreNot()
    {
        var editor = new TextEditor { Text = "ab\tc" };

        var boxed = ConstructSingleCharacterElement(editor, offset: 1);
        Assert.IsInstanceOfType<ControlCharacterBoxElement>(boxed, "The bell character was not boxed.");
        Assert.AreEqual("BEL", TextUtilities.GetControlCharacterName((char)7));
        Assert.IsNull(ConstructSingleCharacterElement(editor, offset: 3), "The tab was boxed.");
    }

    private static VisualLineElement? ConstructSingleCharacterElement(TextEditor editor, int offset)
    {
        var generator = editor.TextArea.TextView.ElementGenerators
            .OfType<SingleCharacterElementGenerator>()
            .SingleOrDefault();
        if (generator is null)
        {
            return null;
        }
        generator.StartGeneration(new GenerationContext(editor));
        try
        {
            return generator.GetFirstInterestedOffset(offset) == offset
                ? generator.ConstructElement(offset)
                : null;
        }
        finally
        {
            generator.FinishGeneration();
        }
    }

    private sealed class GenerationContext(TextEditor editor) : ITextRunConstructionContext
    {
        public TextDocument Document => editor.Document;
        public TextView TextView => editor.TextArea.TextView;
        public DocumentLine CurrentDocumentLine => editor.Document.GetLineByNumber(1);
        public TextRunStyle DefaultStyle => new(editor.FontFamily, editor.FontSize, editor.FontWeight);
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
