using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Draws the vertical rule at the configured column.</summary>
internal sealed class ColumnRulerLayer(TextEditorOptions options, TextEditor editor) : ITextViewLayer
{
    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        if (!options.ShowColumnRuler || options.ColumnRulerPosition <= 0)
        {
            return;
        }

        var view = editor.TextArea.TextView;
        double x = viewportBounds.X + options.ColumnRulerPosition * view.WideSpaceWidth
            - editor.Surface.ScrollOffset.X;
        if (x < viewportBounds.X || x > viewportBounds.Right)
        {
            return;
        }

        // Snapped, or the rule lands between device pixels and shifts as the view scrolls.
        double scale = editor.EditorDpi / 96.0;
        x = LayoutRounding.RoundToPixel(x, scale);
        var rule = view.ResolvedColumnRulerPen;
        var pen = rule with { Thickness = LayoutRounding.SnapThicknessToPixels(rule.Thickness, scale, 1) };

        context.Graphics.DrawLine(
            new Point(x, viewportBounds.Y), new Point(x, viewportBounds.Bottom), pen.ToPen());
    }
}

/// <summary>Paints the line holding the caret, under the text.</summary>
internal sealed class CurrentLineLayer(TextEditorOptions options, TextEditor editor) : ITextViewLayer
{
    public void Draw(ITextRenderContext context, Rect viewportBounds)
    {
        if (!options.HighlightCurrentLine)
        {
            return;
        }

        var surface = editor.Surface;
        int caretLine = editor.Document.GetLocation(editor.CaretOffset).Line;
        var view = editor.TextArea.TextView;
        double scale = editor.EditorDpi / 96.0;
        foreach (var line in surface.VisibleTextLines)
        {
            if (line.LogicalLine.LineNumber + 1 != caretLine)
            {
                continue;
            }

            // Snapped edges, or the band changes height by a pixel from row to row and the
            // highlight appears to jump as the caret moves.
            var bounds = LayoutRounding.SnapRectEdgesToPixels(
                new Rect(
                    viewportBounds.X,
                    viewportBounds.Y + line.DocumentY - surface.ScrollOffset.Y,
                    viewportBounds.Width,
                    line.Height),
                scale);
            var border = view.CurrentLineBorder;
            context.Graphics.FillRectangle(bounds, view.CurrentLineBackground);
            context.Graphics.DrawRectangle(
                bounds,
                (border with
                {
                    Thickness = LayoutRounding.SnapThicknessToPixels(border.Thickness, scale, 1)
                }).ToPen());
            return;
        }
    }
}
