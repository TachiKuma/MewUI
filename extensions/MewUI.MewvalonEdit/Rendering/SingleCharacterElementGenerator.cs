using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Builds the elements that stand in for single characters: a dot for a space, an arrow over a tab,
/// and a box naming a control character. One generator decides all three because a tab is itself a
/// control character, so which marker wins has to be settled in one place.
/// </summary>
internal sealed class SingleCharacterElementGenerator(TextEditorOptions options, TextEditor editor)
    : VisualLineElementGenerator
{
    private const char SPACE_MARKER = '·';
    private const char TAB_MARKER = '→';

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var context = CurrentContext;
        if (context is null)
        {
            return -1;
        }
        var line = context.CurrentDocumentLine;
        int end = line.Offset + line.Length;
        for (int offset = startOffset; offset < end; offset++)
        {
            if (WantsCharacter(context.Document.GetCharAt(offset)))
            {
                return offset;
            }
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var context = CurrentContext;
        if (context is null)
        {
            return null;
        }
        char character = context.Document.GetCharAt(offset);
        var style = new TextRunStyle(editor.FontFamily, editor.FontSize, editor.FontWeight);
        if (character == ' ' && options.ShowSpaces)
        {
            return new WhitespaceMarkerElement(SPACE_MARKER.ToString(), style)
            {
                Foreground = editor.WhitespaceMarkerColor
            };
        }
        if (options.ShowBoxForControlCharacters && char.IsControl(character))
        {
            return new ControlCharacterBoxElement(TextUtilities.GetControlCharacterName(character), style);
        }
        return null;
    }

    /// <summary>
    /// A tab is a control character, but the original settles it before the box ever sees it, so a
    /// tab is never boxed. Its marker is an overlay drawn by the marker layer instead, because an
    /// element here is one run and the original's tab is two: a zero-width glyph and the tab itself.
    /// </summary>
    private bool WantsCharacter(char character) => character switch
    {
        ' ' => options.ShowSpaces,
        '\t' => false,
        _ => options.ShowBoxForControlCharacters && char.IsControl(character)
    };
}

/// <summary>A character drawn as a marker glyph in its place, as the original's space element does.</summary>
internal sealed class WhitespaceMarkerElement(string glyph, TextRunStyle style) : VisualLineElement(1, 1)
{
    private readonly string _glyph = glyph;
    private readonly TextRunStyle _style = style;

    protected internal override string GetVisualText() => _glyph;

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = MarkerLayout.For(_glyph, _style, dpi);
        return new InlineMetrics(layout.MeasuredSize.Width, layout.MeasuredSize.Height, layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new TextDrawOptions(Foreground ?? Color.FromRgb(0x80, 0x80, 0x80));
        context.Draw(MarkerLayout.For(_glyph, _style, dpi), origin, in options);
    }
}

/// <summary>
/// A control character drawn as its name inside a rounded box, which is how the original makes an
/// otherwise invisible character visible without letting it look like ordinary text.
/// </summary>
internal sealed class ControlCharacterBoxElement(string name, TextRunStyle style) : VisualLineElement(1, 1)
{
    private const double HORIZONTAL_PADDING = 3.0;
    private const double CORNER_RADIUS = 2.5;
    private static readonly Color _boxColor = Color.FromArgb(200, 128, 128, 128);
    private static readonly Color _nameColor = Color.FromRgb(255, 255, 255);

    protected internal override string GetVisualText() => name;

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = MarkerLayout.For(name, style, dpi);
        return new InlineMetrics(
            layout.MeasuredSize.Width + HORIZONTAL_PADDING,
            layout.MeasuredSize.Height,
            layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var layout = MarkerLayout.For(name, style, dpi);
        var box = new Rect(
            origin.X,
            origin.Y,
            layout.MeasuredSize.Width + HORIZONTAL_PADDING,
            layout.MeasuredSize.Height);
        context.Graphics.FillRoundedRectangle(box, CORNER_RADIUS, CORNER_RADIUS, _boxColor);
        var options = new TextDrawOptions(_nameColor);
        context.Draw(layout, new Point(origin.X + (HORIZONTAL_PADDING / 2), origin.Y), in options);
    }
}

internal static class MarkerLayout
{
    private static readonly TextParagraphStyle _paragraph = new()
    {
        Wrapping = TextWrapping.NoWrap,
        MaxWidth = double.PositiveInfinity
    };

    public static ITextLayout For(string text, TextRunStyle style, uint dpi)
    {
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        return factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = dpi,
                DefaultStyle = style,
                Paragraph = _paragraph
            },
            TextLayoutCachePolicy.Content);
    }
}
