using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Paints the selection in the editor's own colors, replacing the host's selection layer. Installed
/// only once any of <see cref="TextArea"/>'s selection appearance properties is set, so an editor
/// that leaves them alone keeps the theme's selection untouched.
/// </summary>
internal sealed class SelectionLayer(TextArea textArea) : ITextViewLayer
{
    public Color? Background { get; set; }
    public Color? Foreground { get; set; }
    public Color? Border { get; set; }
    public double CornerRadius { get; set; }

    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        var selection = textArea.Selection;
        if (selection.IsEmpty)
        {
            return;
        }

        var host = textArea.TextView.Host;
        var builder = new BackgroundGeometryBuilder
        {
            CornerRadius = CornerRadius,
            AlignToWholePixels = true,
            BorderThickness = Border.HasValue ? 1 : 0
        };
        foreach (var segment in selection.Segments)
        {
            builder.AddSegment(textArea.TextView, segment);
        }
        var geometry = builder.CreateGeometry();
        if (geometry is null)
        {
            return;
        }

        var graphics = context.Graphics;
        // Falls back to the theme so a layer left installed with its colors cleared paints what the
        // host would have. Without this, clearing SelectionBrush would erase the selection.
        graphics.FillPath(geometry, Background ?? textArea.Editor.ThemeSelectionBackground);
        if (Border is Color border)
        {
            graphics.DrawPath(geometry, border, 1);
        }
        if (Foreground is Color foreground)
        {
            DrawSelectedText(context, host, selection, foreground);
        }
    }

    /// <summary>
    /// Repaints the selected glyphs. Left undone unless a foreground was set: recoloring text
    /// re-segments the runs on every drag frame, which is why the default keeps the glyph colors.
    /// </summary>
    private void DrawSelectedText(
        ITextRenderContext context,
        ITextViewHost host,
        Selection selection,
        Color foreground)
    {
        var viewport = host.TextViewportBounds;
        var scroll = host.ScrollOffset;
        int selectionStart = textArea.Editor.SelectionStart;
        var range = new TextRange(selectionStart, selection.Length);
        foreach (var line in host.VisibleTextLines)
        {
            if (!TextSelectionPresentation.TryCreateSpan(
                    line.LogicalLine, range, foreground, default, out var span))
            {
                continue;
            }
            var origin = new Point(
                viewport.X + line.DocumentX - scroll.X,
                viewport.Y + line.DocumentY - scroll.Y);
            var spans = new TextPaintSpan[] { span with { Background = null } };
            var options = new TextDrawOptions(foreground, spans, Owner: line);
            line.DrawForeground(context, origin, in options);
        }
    }
}
