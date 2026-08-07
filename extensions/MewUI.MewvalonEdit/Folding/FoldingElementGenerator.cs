using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

/// <summary>
/// Produces the line element that stands in for a folded <see cref="FoldingSection"/>.
/// </summary>
public sealed class FoldingElementGenerator(TextEditor editor) : VisualLineElementGenerator
{
    /// <summary>Manager whose foldings are shown, or null to show none.</summary>
    public FoldingManager? FoldingManager { get; set; }

    /// <inheritdoc/>
    public override int GetFirstInterestedOffset(int startOffset)
        => FoldingManager?.GetNextFoldedFoldingStart(startOffset) ?? -1;

    /// <inheritdoc/>
    public override VisualLineElement? ConstructElement(int offset)
    {
        var manager = FoldingManager;
        if (manager is null)
        {
            return null;
        }

        int foldedUntil = -1;
        FoldingSection? foldingSection = null;
        foreach (var section in manager.GetFoldingsContaining(offset))
        {
            if (section.IsFolded && section.EndOffset > foldedUntil)
            {
                foldedUntil = section.EndOffset;
                foldingSection = section;
            }
        }
        if (foldedUntil <= offset || foldingSection is null)
        {
            return null;
        }

        // A folded section starting inside this one can end after it, and the element has to reach
        // that far too or the text between the two ends would be left with no line holding it.
        bool foundOverlappingFolding;
        do
        {
            foundOverlappingFolding = false;
            foreach (var section in manager.GetFoldingsContaining(foldedUntil))
            {
                if (section.IsFolded && section.EndOffset > foldedUntil)
                {
                    foldedUntil = section.EndOffset;
                    foundOverlappingFolding = true;
                }
            }
        } while (foundOverlappingFolding);

        string title = string.IsNullOrEmpty(foldingSection.Title) ? "…" : foldingSection.Title!;
        var style = new TextRunStyle(editor.FontFamily, editor.FontSize, editor.FontWeight);
        return new FoldingLineElement(foldingSection, title, foldedUntil - offset, style)
        {
            Foreground = editor.FoldingMarkerColor
        };
    }

    /// <summary>The folded section's title, outlined so it does not read as ordinary text.</summary>
    private sealed class FoldingLineElement(
        FoldingSection section,
        string title,
        int documentLength,
        TextRunStyle style)
        : TextReplacementElement(title, documentLength, style)
    {
        private const double CORNER_RADIUS = 2;

        public override void Draw(ITextRenderContext context, Point origin, uint dpi)
        {
            ArgumentNullException.ThrowIfNull(context);
            var metrics = Measure(dpi);
            double dpiScale = dpi / 96.0;
            var pen = new ColorPen(Foreground ?? Color.FromRgb(0x80, 0x80, 0x80)).SnapThickness(dpiScale);
            // The stroke sits on the edge it is given, so the box is inset by half of it; without
            // that it straddles the snapped edge and covers a pixel on either side.
            var box = LayoutRounding.SnapBoundsRectToPixels(
                new Rect(origin.X, origin.Y, metrics.Width, metrics.Height), dpiScale);
            context.Graphics.DrawRoundedRectangle(
                box.Deflate(new Thickness(pen.Thickness / 2)),
                CORNER_RADIUS,
                CORNER_RADIUS,
                pen.Color,
                pen.Thickness);
            base.Draw(context, origin, dpi);
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e.ClickCount == 2 && e.Button == MouseButton.Left)
            {
                section.IsFolded = false;
                e.Handled = true;
            }
            else
            {
                base.OnMouseDown(e);
            }
        }
    }
}
