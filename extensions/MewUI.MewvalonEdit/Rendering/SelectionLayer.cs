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
    public Color? Border { get; set; }

    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        var selection = textArea.Selection;
        if (selection.IsEmpty)
        {
            return;
        }

        var builder = new BackgroundGeometryBuilder
        {
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
    }
}
