using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Maps document segments to view rectangles and geometry, as AvalonEdit's builder does for background renderers.</summary>
public sealed class BackgroundGeometryBuilder
{
    private readonly List<Rect> _rectangles = [];

    /// <summary>Radius of the geometry's corners.</summary>
    public double CornerRadius { get; set; }

    /// <summary>Snaps rectangle edges to whole pixels, keeping 1px marker borders crisp.</summary>
    public bool AlignToWholePixels { get; set; }

    /// <summary>Half of this inset is taken off each rectangle so a stroked border stays inside it.</summary>
    public double BorderThickness { get; set; }

    /// <summary>Extends rectangles that reach a line end to the right edge of the viewport.</summary>
    public bool ExtendToFullWidthAtLineEnd { get; set; }

    /// <summary>Adds the visible rectangles of the segment.</summary>
    public void AddSegment(TextView textView, ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(segment);
        foreach (var rect in GetRectsCore(textView, segment.Offset, segment.EndOffset, ExtendToFullWidthAtLineEnd))
        {
            AddRectangle(textView, rect);
        }
    }

    /// <summary>
    /// Adds one rectangle in view coordinates, aligned per <see cref="AlignToWholePixels"/>. The
    /// view supplies the pixel size; use the four-coordinate overload for already aligned input.
    /// </summary>
    public void AddRectangle(TextView textView, Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(textView);
        if (!AlignToWholePixels)
        {
            AddRectangle(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            return;
        }

        // Rounded on the outer edge and offset back by half the border, so a stroke of that width
        // sits centred on a device pixel instead of straddling two.
        double scale = textView.DpiScale;
        double halfBorder = 0.5 * BorderThickness;
        AddRectangle(
            LayoutRounding.RoundToPixel(rectangle.Left - halfBorder, scale) + halfBorder,
            LayoutRounding.RoundToPixel(rectangle.Top - halfBorder, scale) + halfBorder,
            LayoutRounding.RoundToPixel(rectangle.Right + halfBorder, scale) - halfBorder,
            LayoutRounding.RoundToPixel(rectangle.Bottom + halfBorder, scale) - halfBorder);
    }

    /// <summary>Adds one rectangle whose coordinates are already aligned.</summary>
    public void AddRectangle(double left, double top, double right, double bottom)
    {
        if (right > left && bottom > top)
        {
            _rectangles.Add(new Rect(left, top, right - left, bottom - top));
        }
    }

    /// <summary>Geometry of everything added so far, or null when nothing was added.</summary>
    public PathGeometry? CreateGeometry()
    {
        if (_rectangles.Count == 0)
        {
            return null;
        }
        var geometry = new PathGeometry();
        foreach (var rect in _rectangles)
        {
            double radius = Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2);
            if (radius <= 0)
            {
                geometry.MoveTo(rect.X, rect.Y);
                geometry.LineTo(rect.Right, rect.Y);
                geometry.LineTo(rect.Right, rect.Bottom);
                geometry.LineTo(rect.X, rect.Bottom);
                geometry.Close();
            }
            else
            {
                geometry.MoveTo(rect.X + radius, rect.Y);
                geometry.LineTo(rect.Right - radius, rect.Y);
                geometry.ArcTo(rect.Right, rect.Y, rect.Right, rect.Y + radius, radius);
                geometry.LineTo(rect.Right, rect.Bottom - radius);
                geometry.ArcTo(rect.Right, rect.Bottom, rect.Right - radius, rect.Bottom, radius);
                geometry.LineTo(rect.X + radius, rect.Bottom);
                geometry.ArcTo(rect.X, rect.Bottom, rect.X, rect.Bottom - radius, radius);
                geometry.LineTo(rect.X, rect.Y + radius);
                geometry.ArcTo(rect.X, rect.Y, rect.X + radius, rect.Y, radius);
                geometry.Close();
            }
        }
        return geometry;
    }

    /// <summary>Rectangles covering the visible parts of <paramref name="segment"/>, in view coordinates.</summary>
    public static IEnumerable<Rect> GetRectsForSegment(
        TextView textView,
        ISegment segment,
        bool extendToFullWidthAtLineEnd = false)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(segment);
        return GetRectsCore(textView, segment.Offset, segment.EndOffset, extendToFullWidthAtLineEnd);
    }

    private static List<Rect> GetRectsCore(TextView textView, int startOffset, int endOffset, bool extendToFullWidth)
    {
        var result = new List<Rect>();
        var surface = textView.Surface;
        var viewport = surface.TextViewportBounds;
        var bounds = new List<Rect>();
        foreach (var line in surface.VisibleTextLines)
        {
            var logical = line.LogicalLine;
            int lineStart = logical.Offset;
            int lineEnd = lineStart + logical.Length;
            if (endOffset <= lineStart || startOffset > lineEnd)
            {
                continue;
            }

            int from = Math.Max(startOffset, lineStart) - lineStart;
            int to = Math.Min(endOffset, lineEnd) - lineStart;
            double lineTop = viewport.Y + line.DocumentY - surface.VerticalOffset;
            double left = viewport.X - surface.HorizontalOffset;

            if (to <= from)
            {
                // Zero-length or line-end match: fall back to the caret slot so empty
                // selections and end-of-line markers still produce a rectangle.
                var caret = line.GetCaretBounds(new CharacterHit(Math.Clamp(from, 0, logical.Length), 0));
                result.Add(new Rect(left + caret.X, lineTop + caret.Y, extendToFullWidth ? viewport.Width : 1, caret.Height));
                continue;
            }

            bounds.Clear();
            line.GetRangeBounds(new TextRange(from, to - from), bounds);
            foreach (var rect in bounds)
            {
                double width = extendToFullWidth && to >= logical.Length
                    ? Math.Max(rect.Width, viewport.Right - (left + rect.X))
                    : rect.Width;
                result.Add(new Rect(left + rect.X, lineTop + rect.Y, width, rect.Height));
            }
        }
        return result;
    }
}
