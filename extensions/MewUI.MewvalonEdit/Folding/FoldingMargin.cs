using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

/// <summary>
/// Gutter with expand and collapse boxes, one per folding that starts on a visible line. Clicking a
/// box toggles the folding.
/// </summary>
public sealed class FoldingMargin : AbstractMargin
{
    private const double MARGIN_WIDTH = 16;
    private const double BOX_SIZE = 9;

    private FoldingManager? _foldingManager;

    /// <summary>Manager whose foldings the margin shows. Assigned by the installing manager.</summary>
    public FoldingManager? FoldingManager
    {
        get => _foldingManager;
        set
        {
            if (ReferenceEquals(_foldingManager, value))
            {
                return;
            }
            if (_foldingManager is not null)
            {
                _foldingManager.FoldingsChanged -= OnFoldingsChanged;
            }
            _foldingManager = value;
            if (value is not null)
            {
                value.FoldingsChanged += OnFoldingsChanged;
            }
            InvalidateVisual();
        }
    }

    public Color FoldingMarkerBrush { get; set; } = Color.FromRgb(128, 128, 128);
    public Color FoldingMarkerBackgroundBrush { get; set; } = Color.Transparent;

    protected override Size MeasureContent(Size availableSize)
        => new(MARGIN_WIDTH, Math.Max(1, availableSize.Height));

    protected override void OnRenderTextViewport(IGraphicsContext context, Rect textViewport)
    {
        foreach ((var section, var box) in EnumerateBoxes(textViewport))
        {
            if (FoldingMarkerBackgroundBrush.A > 0)
            {
                context.FillRectangle(box, FoldingMarkerBackgroundBrush);
            }
            context.DrawRectangle(box, FoldingMarkerBrush, 1);
            double middleY = box.Y + box.Height / 2;
            context.DrawLine(
                new Point(box.X + 2, middleY), new Point(box.Right - 2, middleY), FoldingMarkerBrush, 1);
            if (section.IsFolded)
            {
                double middleX = box.X + box.Width / 2;
                context.DrawLine(
                    new Point(middleX, box.Y + 2), new Point(middleX, box.Bottom - 2), FoldingMarkerBrush, 1);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || TextView is null)
        {
            return;
        }
        var position = e.GetPosition(this);
        var windowPoint = new Point(position.X + Bounds.X, position.Y + Bounds.Y);
        foreach ((var section, var box) in EnumerateBoxes(TextView.Host.TextViewportBounds))
        {
            if (box.Inflate(new Thickness(2)).Contains(windowPoint))
            {
                section.IsFolded = !section.IsFolded;
                e.Handled = true;
                return;
            }
        }
    }

    private IEnumerable<(FoldingSection Section, Rect Box)> EnumerateBoxes(Rect textViewport)
    {
        if (FoldingManager is not FoldingManager manager || TextView is not TextView view)
        {
            yield break;
        }

        double scrollY = view.Host.ScrollOffset.Y;
        double boxX = Bounds.X + (Bounds.Width - BOX_SIZE) / 2;
        foreach (var line in view.Host.VisibleTextLines)
        {
            var logical = line.LogicalLine;
            var section = manager.GetNextFolding(logical.Offset);
            if (section is null || section.StartOffset >= logical.Offset + logical.Length + 1)
            {
                continue;
            }

            double rowHeight = line.VisualLines.Count > 0 ? line.VisualLines[0].Bounds.Height : line.Height;
            double boxY = textViewport.Y + line.DocumentY - scrollY + (rowHeight - BOX_SIZE) / 2;
            yield return (section, new Rect(boxX, boxY, BOX_SIZE, BOX_SIZE));
        }
    }

    private void OnFoldingsChanged(object? sender, EventArgs e) => InvalidateVisual();
}
