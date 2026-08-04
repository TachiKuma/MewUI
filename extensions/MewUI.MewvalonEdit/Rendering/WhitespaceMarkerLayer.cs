using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Draws tab and end-of-line markers over the laid-out lines. They are drawn rather than substituted
/// into the text so a tab keeps its tab-stop width instead of collapsing to one glyph; it mirrors
/// AvalonEdit's zero-width TabGlyphRun, which overlays the arrow and leaves the real tab in the run.
/// Spaces are substituted by <see cref="SpaceMarkerProjection"/>, as AvalonEdit does.
/// </summary>
internal sealed class WhitespaceMarkerLayer(TextEditorOptions options, TextEditor editor) : ITextViewLayer
{
    private const char TAB_MARKER = '→';
    private const char END_OF_LINE_MARKER = '¶';

    private static readonly TextParagraphStyle _markerParagraph = new()
    {
        Wrapping = TextWrapping.NoWrap,
        MaxWidth = double.PositiveInfinity
    };

    private readonly List<Rect> _bounds = [];

    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        if (!options.ShowTabs && !options.ShowEndOfLine)
        {
            return;
        }

        var surface = editor.Surface;
        var lines = surface.VisibleTextLines;
        if (lines.Count == 0)
        {
            return;
        }

        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var engine = factory.TextEngine;
        uint dpi = editor.GetDpi();
        var style = new TextRunStyle(editor.FontFamily, editor.FontSize, editor.FontWeight);
        var drawOptions = new TextDrawOptions(editor.WhitespaceMarkerColor);
        var scroll = surface.ScrollOffset;

        foreach (var line in lines)
        {
            double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
            var origin = new Point(
                viewportBounds.X - scroll.X,
                viewportBounds.Y + documentY - scroll.Y);
            DrawLineMarkers(context, engine, line, origin, dpi, style, in drawOptions);
        }
    }

    private void DrawLineMarkers(
        ITextRenderContext context,
        ITextEngine engine,
        TextLineLayout line,
        Point origin,
        uint dpi,
        TextRunStyle style,
        in TextDrawOptions drawOptions)
    {
        int length = line.LogicalLine.Length;
        if (options.ShowTabs)
        {
            var text = editor.Document.GetText(line.LogicalLine.Offset, length);
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\t')
                {
                    DrawMarker(context, engine, line, origin, dpi, style, in drawOptions, index, TAB_MARKER, true);
                }
            }
        }
        if (options.ShowEndOfLine)
        {
            DrawMarker(context, engine, line, origin, dpi, style, in drawOptions, length, END_OF_LINE_MARKER, false);
        }
    }

    private void DrawMarker(
        ITextRenderContext context,
        ITextEngine engine,
        TextLineLayout line,
        Point origin,
        uint dpi,
        TextRunStyle style,
        in TextDrawOptions drawOptions,
        int offset,
        char glyph,
        bool occupiesCell)
    {
        Rect cell;
        if (occupiesCell)
        {
            _bounds.Clear();
            line.GetRangeBounds(new TextRange(offset, 1), _bounds);
            if (_bounds.Count == 0)
            {
                return;
            }
            cell = _bounds[0];
        }
        else
        {
            // The end-of-line marker sits past the last character, so it has no cell to measure and
            // its position comes from the caret instead.
            cell = line.GetCaretBounds(new CharacterHit(offset, 0));
        }

        var layout = engine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = glyph.ToString().AsMemory(),
                Dpi = dpi,
                DefaultStyle = style,
                Paragraph = _markerParagraph
            },
            TextLayoutCachePolicy.Content);

        // Both markers start at the cell edge: the tab arrow marks where the tab begins, as
        // AvalonEdit draws it, and the end-of-line marker follows the last character.
        context.Draw(layout, new Point(origin.X + cell.X, origin.Y + cell.Y), in drawOptions);
    }
}
