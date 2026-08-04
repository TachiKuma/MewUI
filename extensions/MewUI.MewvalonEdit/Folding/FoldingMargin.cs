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

    public static readonly MewProperty<Color?> FoldingMarkerBrushProperty =
        MewProperty<Color?>.Register<FoldingMargin>(nameof(FoldingMarkerBrush), null,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color?> FoldingMarkerBackgroundBrushProperty =
        MewProperty<Color?>.Register<FoldingMargin>(nameof(FoldingMarkerBackgroundBrush), null,
            MewPropertyOptions.AffectsRender);

    /// <summary>Outline and line colour. Null follows the theme, so it tracks light and dark.</summary>
    public Color? FoldingMarkerBrush
    {
        get => GetValue(FoldingMarkerBrushProperty);
        set => SetValue(FoldingMarkerBrushProperty, value);
    }

    /// <summary>
    /// Fill of the marker box. Null takes the theme's control background, which is what keeps the
    /// extent line from running through the box; AvalonEdit fills it with a solid brush for the
    /// same reason.
    /// </summary>
    public Color? FoldingMarkerBackgroundBrush
    {
        get => GetValue(FoldingMarkerBackgroundBrushProperty);
        set => SetValue(FoldingMarkerBackgroundBrushProperty, value);
    }

    private Color ResolvedMarker => FoldingMarkerBrush ?? Theme.Palette.PlaceholderText;
    private Color ResolvedMarkerBackground => FoldingMarkerBackgroundBrush ?? Theme.Palette.ControlBackground;

    protected override Size MeasureContent(Size availableSize)
        => new(MARGIN_WIDTH, Math.Max(1, availableSize.Height));

    protected override void OnRenderTextViewport(IGraphicsContext context, Rect textViewport)
    {
        double middleX = Bounds.X + Bounds.Width / 2;
        foreach ((var section, var box) in EnumerateBoxes(textViewport))
        {
            // An expanded section marks how far it reaches with a line running down to its end
            // row, so the gutter shows the extent the box would collapse.
            if (!section.IsFolded)
            {
                double endY = ResolveEndY(section, textViewport);
                if (endY > box.Bottom)
                {
                    context.DrawLine(
                        new Point(middleX, box.Bottom), new Point(middleX, endY), ResolvedMarker, 1);
                    context.DrawLine(
                        new Point(middleX, endY), new Point(middleX + BOX_SIZE / 2, endY), ResolvedMarker, 1);
                }
            }

            // Filled before the outline so the extent line drawn above stops at the box edge.
            context.FillRectangle(box, ResolvedMarkerBackground);
            context.DrawRectangle(box, ResolvedMarker, 1);
            double middleY = box.Y + box.Height / 2;
            context.DrawLine(
                new Point(box.X + 2, middleY), new Point(box.Right - 2, middleY), ResolvedMarker, 1);
            if (section.IsFolded)
            {
                double centerX = box.X + box.Width / 2;
                context.DrawLine(
                    new Point(centerX, box.Y + 2), new Point(centerX, box.Bottom - 2), ResolvedMarker, 1);
            }
        }
    }

    /// <summary>
    /// Screen Y of the section's last row, clamped to the viewport so a section running past the
    /// bottom still draws a line all the way down.
    /// </summary>
    private double ResolveEndY(FoldingSection section, Rect textViewport)
    {
        if (TextView is not TextView view)
        {
            return 0;
        }
        var document = view.Document;
        int endOffset = Math.Clamp(section.EndOffset, 0, document.TextLength);
        int endLine = document.GetLocation(endOffset).Line;
        double documentY = view.GetVisualTopByDocumentLine(endLine);
        double screenY = textViewport.Y + documentY - view.Host.ScrollOffset.Y + view.DefaultLineHeight / 2;
        return Math.Min(screenY, textViewport.Bottom);
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
