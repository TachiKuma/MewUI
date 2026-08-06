using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Draws the tab and end-of-line markers over the laid-out lines. Neither can be an element: the
/// end-of-line position holds no character to stand in for, and the original's tab is two runs, a
/// zero-width glyph followed by the tab itself, where an element here contributes one. Substituting
/// the tab instead would collapse it to the arrow's width and lose its tab stop.
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
        uint dpi = editor.GetDpi();
        var style = new TextRunStyle(editor.FontFamily, editor.FontSize, editor.FontWeight);
        var drawOptions = new TextDrawOptions(editor.WhitespaceMarkerColor);
        var scroll = surface.ScrollOffset;
        var bounds = new List<Rect>();

        foreach (var line in lines)
        {
            double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
            var origin = new Point(
                viewportBounds.X - scroll.X,
                viewportBounds.Y + documentY - scroll.Y);
            if (options.ShowTabs)
            {
                string text = editor.Document.GetText(line.LogicalLine.Offset, line.LogicalLine.Length);
                for (int index = 0; index < text.Length; index++)
                {
                    if (text[index] != '\t')
                    {
                        continue;
                    }
                    bounds.Clear();
                    line.GetRangeBounds(new TextRange(index, 1), bounds);
                    if (bounds.Count > 0)
                    {
                        // At the cell edge, where the tab begins, as the original draws it.
                        Draw(context, factory, TAB_MARKER, style, dpi, in drawOptions,
                            new Point(origin.X + bounds[0].X, origin.Y + bounds[0].Y));
                    }
                }
            }
            if (options.ShowEndOfLine)
            {
                // Past the last character, so the position comes from the caret rather than a cell.
                var caret = line.GetCaretBounds(new CharacterHit(line.LogicalLine.Length, 0));
                Draw(context, factory, END_OF_LINE_MARKER, style, dpi, in drawOptions,
                    new Point(origin.X + caret.X, origin.Y + caret.Y));
            }
        }
    }

    private static void Draw(
        ITextRenderContext context,
        IGraphicsFactory factory,
        char glyph,
        TextRunStyle style,
        uint dpi,
        in TextDrawOptions drawOptions,
        Point origin)
    {
        var layout = factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = glyph.ToString().AsMemory(),
                Dpi = dpi,
                DefaultStyle = style,
                Paragraph = _markerParagraph
            },
            TextLayoutCachePolicy.Content);
        context.Draw(layout, origin, in drawOptions);
    }
}
