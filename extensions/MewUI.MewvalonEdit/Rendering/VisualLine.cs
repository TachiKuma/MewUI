using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// One laid-out line of the view: the document line, the elements generators built on it, and the
/// offset-to-visual-column mapping. Mirrors AvalonEdit's type over a materialized engine line;
/// visual columns are projected offsets of the engine's offset map.
/// </summary>
public sealed class VisualLine
{
    private readonly TextLineLayout _layout;

    internal VisualLine(TextLineLayout layout, DocumentLine firstDocumentLine, IReadOnlyList<VisualLineElement> elements)
    {
        _layout = layout;
        FirstDocumentLine = firstDocumentLine;
        Elements = elements;
    }

    public DocumentLine FirstDocumentLine { get; }

    /// <summary>Elements the generators produced on this line, in document order.</summary>
    public IReadOnlyList<VisualLineElement> Elements { get; }

    /// <summary>Document offset the laid-out range starts at. Mid-line for a virtualized slice.</summary>
    public int StartOffset => _layout.LogicalLine.Offset;

    /// <summary>Length of the laid-out document range.</summary>
    public int DocumentLength => _layout.LogicalLine.Length;

    /// <summary>Length of the line on the visual surface, after projections.</summary>
    public int VisualLength => _layout.LogicalLine.TotalLength;

    /// <summary>Top of the line in document coordinates.</summary>
    public double VisualTop => _layout.DocumentY;

    public double Height => _layout.Height;

    /// <summary>Visual column of a document offset relative to <see cref="StartOffset"/>.</summary>
    public int GetVisualColumn(int relativeTextOffset)
        => _layout.MapSourceOffsetToProjected(Math.Clamp(relativeTextOffset, 0, DocumentLength));

    /// <summary>Document offset (relative to <see cref="StartOffset"/>) of a visual column.</summary>
    public int GetRelativeOffset(int visualColumn)
        => _layout.MapProjectedOffsetToSource(ValidateVisualColumn(visualColumn));

    /// <summary>Clamps a visual column into this line.</summary>
    public int ValidateVisualColumn(int visualColumn)
        => Math.Clamp(visualColumn, 0, VisualLength);

    internal TextLineLayout Layout => _layout;
}
