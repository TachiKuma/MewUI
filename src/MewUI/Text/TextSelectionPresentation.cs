namespace Aprillz.MewUI.Text;

/// <summary>Converts a document selection into paint-only spans for a logical line.</summary>
public static class TextSelectionPresentation
{
    public static bool TryCreateSpan(
        LogicalTextLine line,
        TextRange documentSelection,
        Color foreground,
        Color background,
        out TextPaintSpan span)
    {
        int lineStart = line.Offset;
        int lineEnd = lineStart + line.Length;
        int start = Math.Max(documentSelection.Start, lineStart);
        int end = Math.Min(documentSelection.End, lineEnd);
        if (end <= start)
        {
            span = default;
            return false;
        }
        span = new TextPaintSpan(
            new TextRange(start - lineStart, end - start),
            foreground,
            background);
        return true;
    }
}
