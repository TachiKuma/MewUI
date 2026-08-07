using System.Text;
using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

/// <summary>
/// A selection that spans a column range over several lines rather than a run of offsets. It is
/// built from two x positions rather than two offsets, so lines of different length all give up the
/// same columns, and it uses virtual space whatever <see cref="TextEditorOptions.EnableVirtualSpace"/>
/// says: a column past the end of a short line is still part of the rectangle.
/// </summary>
public sealed class RectangleSelection : Selection
{
    private readonly int _startLine;
    private readonly int _endLine;
    private readonly double _startX;
    private readonly double _endX;
    private readonly List<SelectionSegment> _segments = [];
    private readonly TextViewPosition _start;
    private readonly TextViewPosition _end;

    public RectangleSelection(TextArea textArea, TextViewPosition start, TextViewPosition end)
        : this(textArea, start.Line, GetX(textArea, start), end.Line, GetX(textArea, end))
    {
        _start = start;
        _end = end;
    }

    private RectangleSelection(TextArea textArea, int startLine, double startX, int endLine, double endX)
        : base(textArea)
    {
        _startLine = startLine;
        _endLine = endLine;
        _startX = startX;
        _endX = endX;
        CalculateSegments();
        _start = ResolveStart();
        _end = ResolveEnd();
    }

    /// <summary>Where the rectangle was started from, which is the corner the caret left behind.</summary>
    public override TextViewPosition StartPosition => _start;

    /// <summary>Where the rectangle currently reaches, which is the corner the caret is at.</summary>
    public override TextViewPosition EndPosition => _end;

    /// <summary>One range per line the rectangle covers, in line order.</summary>
    public override IEnumerable<SelectionSegment> Segments => _segments;

    /// <summary>
    /// Everything from the first line's start column to the last line's end column, which is the
    /// range the rectangle is contained in rather than the range it selects.
    /// </summary>
    public override ISegment? SurroundingSegment
        => _segments.Count == 0
            ? null
            : new SelectionSegment(_segments[0].StartOffset, _segments[^1].EndOffset);

    /// <summary>Always true: a rectangle selects columns, and a short line has to give up the same ones.</summary>
    public override bool EnableVirtualSpace => true;

    public override int Length => _segments.Sum(static segment => segment.Length);

    public override string GetText()
    {
        var text = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }
            text.Append(TextArea.Document.GetText(segment.StartOffset, segment.Length));
        }
        return text.ToString();
    }

    public override Selection SetEndpoint(TextViewPosition endPosition)
        => new RectangleSelection(TextArea, _startLine, _startX, endPosition.Line, GetX(TextArea, endPosition));

    public override Selection StartSelectionOrSetEndpoint(
        TextViewPosition startPosition, TextViewPosition endPosition)
        => SetEndpoint(endPosition);

    /// <summary>
    /// The rectangle over the changed document. Both corners are carried across the change and the
    /// columns are worked out again from them, since a change on one line moves the columns of that
    /// line only.
    /// </summary>
    public override Selection UpdateOnDocumentChange(DocumentChangeEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var document = TextArea.Document;
        int startOffset = e.GetNewOffset(document.GetOffset(_start.Line, _start.Column), AnchorMovementType.Default);
        int endOffset = e.GetNewOffset(document.GetOffset(_end.Line, _end.Column), AnchorMovementType.Default);
        return new RectangleSelection(
            TextArea,
            new TextViewPosition(document.GetLocation(startOffset), _start.VisualColumn),
            new TextViewPosition(document.GetLocation(endOffset), _end.VisualColumn));
    }

    /// <summary>
    /// Replaces every line's range with the text, as one undo step. Typing over a rectangle changes
    /// each line it covers, which is the whole point of it.
    /// </summary>
    public override void ReplaceSelectionWithText(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        using var group = TextArea.Document.UndoStack.OpenUndoGroup();
        // Bottom up, so replacing one line does not move the ranges of the lines still to come.
        for (int index = _segments.Count - 1; index >= 0; index--)
        {
            var segment = _segments[index];
            TextArea.Document.Replace(segment.StartOffset, segment.Length, newText);
        }
    }

    public override bool Equals(object? obj)
        => obj is RectangleSelection other
            && _startLine == other._startLine
            && _endLine == other._endLine
            && _startX.Equals(other._startX)
            && _endX.Equals(other._endX)
            && ReferenceEquals(TextArea, other.TextArea);

    public override int GetHashCode() => HashCode.Combine(_startLine, _endLine, _startX, _endX, TextArea);

    public override string ToString()
        => $"[RectangleSelection {_startLine} {_startX} to {_endLine} {_endX}]";

    /// <summary>
    /// Where a position sits across the view, which is what a rectangle is made of. Two positions on
    /// different lines with the same x belong to the same column of the rectangle however much tab
    /// or marker width lies before them.
    /// </summary>
    private static double GetX(TextArea textArea, TextViewPosition position)
    {
        var line = textArea.Document.GetLineByNumber(position.Line);
        var visualLine = textArea.TextView.GetOrConstructVisualLine(line);
        return visualLine is null
            ? 0
            : visualLine.GetVisualXPosition(visualLine.ValidateVisualColumn(position, allowVirtualSpace: true));
    }

    private void CalculateSegments()
    {
        var document = TextArea.Document;
        int first = Math.Min(_startLine, _endLine);
        int last = Math.Max(_startLine, _endLine);
        for (int lineNumber = first; lineNumber <= last && lineNumber <= document.LineCount; lineNumber++)
        {
            var visualLine = TextArea.TextView.GetOrConstructVisualLine(document.GetLineByNumber(lineNumber));
            if (visualLine is null)
            {
                continue;
            }
            int startColumn = visualLine.GetVisualColumn(new Point(_startX, 0), allowVirtualSpace: true);
            int endColumn = visualLine.GetVisualColumn(new Point(_endX, 0), allowVirtualSpace: true);
            int baseOffset = visualLine.FirstDocumentLine.Offset;
            _segments.Add(new SelectionSegment(
                baseOffset + visualLine.GetRelativeOffset(startColumn),
                startColumn,
                baseOffset + visualLine.GetRelativeOffset(endColumn),
                endColumn));
        }
    }

    private TextViewPosition ResolveStart() => ResolveCorner(_startLine < _endLine, _startX < _endX);

    private TextViewPosition ResolveEnd() => ResolveCorner(_startLine >= _endLine, _startX >= _endX);

    /// <summary>
    /// One corner of the rectangle: which line it is on follows the direction the rectangle was
    /// drawn in, and which end of that line follows the direction across it.
    /// </summary>
    private TextViewPosition ResolveCorner(bool takeFirstLine, bool takeStartColumn)
    {
        if (_segments.Count == 0)
        {
            return default;
        }
        var segment = takeFirstLine ? _segments[0] : _segments[^1];
        var document = TextArea.Document;
        return takeStartColumn
            ? new TextViewPosition(document.GetLocation(segment.StartOffset), segment.StartVisualColumn)
            : new TextViewPosition(document.GetLocation(segment.EndOffset), segment.EndVisualColumn);
    }
}
