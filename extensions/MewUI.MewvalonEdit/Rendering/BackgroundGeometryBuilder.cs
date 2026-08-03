using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Maps document segments to view rectangles, as AvalonEdit's builder does for background renderers.</summary>
public static class BackgroundGeometryBuilder
{
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
