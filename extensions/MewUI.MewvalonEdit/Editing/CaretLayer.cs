using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

/// <summary>
/// Draws the caret in place of the editing surface's own. Taking the anchor is what lets the caret
/// be coloured, hidden, and widened for overstrike; the blink itself stays the surface's, so both
/// agree on when the caret is in its visible phase.
/// </summary>
internal sealed class CaretLayer(TextArea textArea) : ITextViewLayer
{
    // The overwritten character has to stay readable under the caret covering it.
    private const byte OVERSTRIKE_ALPHA = 100;
    private const double MINIMUM_WIDTH = 1;

    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        var surface = textArea.Editor.Surface;
        if (!textArea.Caret.IsVisible || !surface.IsFocused || !surface.CaretVisible)
        {
            return;
        }

        var rectangle = GetCaretRectangle(surface);
        if (rectangle.IsEmpty)
        {
            return;
        }

        var color = textArea.Caret.CaretBrush ?? textArea.Editor.Foreground;
        if (textArea.OverstrikeMode)
        {
            color = Color.FromArgb(OVERSTRIKE_ALPHA, color.R, color.G, color.B);
        }
        context.Graphics.FillRectangle(rectangle, color);
    }

    /// <summary>
    /// A window-coordinate rectangle: one column wide normally, and as wide as the character it
    /// would overwrite in overstrike mode.
    /// </summary>
    private Rect GetCaretRectangle(Controls.MultiLineTextBox surface)
    {
        int offset = textArea.Caret.Offset;
        var caret = surface.GetCharRectInWindow(offset);
        if (caret.IsEmpty)
        {
            return Rect.Empty;
        }

        double width = MINIMUM_WIDTH;
        if (textArea.OverstrikeMode)
        {
            var line = textArea.Document.GetLineByOffset(offset);
            // Nothing to overwrite past the last character, so the caret stays a thin one there.
            if (offset < line.EndOffset)
            {
                var next = surface.GetCharRectInWindow(offset + 1);
                if (next.Y == caret.Y)
                {
                    width = Math.Max(MINIMUM_WIDTH, next.X - caret.X);
                }
            }
        }
        return new Rect(caret.X, caret.Y, width, Math.Max(MINIMUM_WIDTH, caret.Height));
    }
}
