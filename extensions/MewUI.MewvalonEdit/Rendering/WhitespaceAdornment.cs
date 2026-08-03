using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Draws tab and end-of-line markers over the laid-out line. These are adornments rather than a
/// text projection so a tab keeps its tab-stop width instead of collapsing to one glyph; it mirrors
/// AvalonEdit's zero-width TabGlyphRun, which overlays the arrow and leaves the real tab in the run.
/// Spaces are substituted by <see cref="SpaceMarkerProjection"/>, as AvalonEdit does.
/// </summary>
internal sealed class WhitespaceAdornmentProvider(TextEditorOptions options, TextEditor editor) : ITextAdornmentProvider
{
    private const char TAB_MARKER = '→';
    private const char END_OF_LINE_MARKER = '¶';

    public void GetAdornments(in TextAdornmentContext context, IList<ITextAdornment> output)
    {
        if (!options.ShowTabs && !options.ShowEndOfLine)
        {
            return;
        }

        var text = context.Text.Span;
        List<WhitespaceMarker>? markers = null;
        if (options.ShowTabs)
        {
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\t')
                {
                    (markers ??= []).Add(new WhitespaceMarker(index, TAB_MARKER, OccupiesCell: true));
                }
            }
        }

        if (options.ShowEndOfLine)
        {
            (markers ??= []).Add(new WhitespaceMarker(text.Length, END_OF_LINE_MARKER, OccupiesCell: false));
        }

        if (markers is not null)
        {
            output.Add(new WhitespaceAdornment(markers, editor));
        }
    }
}

// OccupiesCell is false for the end-of-line marker: it sits past the last character, so it has no
// character cell to measure and its position comes from the caret instead.
internal readonly record struct WhitespaceMarker(int Offset, char Glyph, bool OccupiesCell);

internal sealed class WhitespaceAdornment(
    IReadOnlyList<WhitespaceMarker> markers,
    TextEditor editor) : ITextAdornment
{
    private static readonly TextParagraphStyle MarkerParagraph = new()
    {
        Wrapping = TextWrapping.NoWrap,
        MaxWidth = double.PositiveInfinity
    };

    public TextAdornmentLayer Layer => TextAdornmentLayer.Text;

    public void Draw(ITextRenderContext context, TextLineLayout line, Point origin)
    {
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var engine = factory.TextEngine;
        uint dpi = editor.GetDpi();
        var style = new TextRunStyle(editor.FontFamily, editor.FontSize, editor.FontWeight);
        var drawOptions = new TextDrawOptions(editor.WhitespaceMarkerColor);
        var bounds = new List<Rect>();

        foreach (var marker in markers)
        {
            Rect cell;
            if (marker.OccupiesCell)
            {
                bounds.Clear();
                line.GetRangeBounds(new TextRange(marker.Offset, 1), bounds);
                if (bounds.Count == 0)
                {
                    continue;
                }
                cell = bounds[0];
            }
            else
            {
                cell = line.GetCaretBounds(new CharacterHit(marker.Offset, 0));
            }

            var layout = engine.GetOrCreateLayout(
                new TextLayoutRequest
                {
                    Text = marker.Glyph.ToString().AsMemory(),
                    Dpi = dpi,
                    DefaultStyle = style,
                    Paragraph = MarkerParagraph
                },
                TextLayoutCachePolicy.Content);

            // Both markers start at the cell edge: the tab arrow marks where the tab begins, as
            // AvalonEdit draws it, and the end-of-line marker follows the last character.
            context.Draw(layout, new Point(origin.X + cell.X, origin.Y + cell.Y), in drawOptions);
        }
    }
}
