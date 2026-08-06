using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit;

/// <summary>A document location together with the visual column it lands on.</summary>
public struct TextViewPosition : IEquatable<TextViewPosition>, IComparable<TextViewPosition>
{
    public TextViewPosition(int line, int column, int visualColumn)
    {
        Line = line;
        Column = column;
        VisualColumn = visualColumn;
        IsAtEndOfLine = false;
    }

    public TextViewPosition(int line, int column)
        : this(line, column, -1)
    {
    }

    public TextViewPosition(TextLocation location, int visualColumn)
        : this(location.Line, location.Column, visualColumn)
    {
    }

    public TextViewPosition(TextLocation location)
        : this(location, -1)
    {
    }

    public TextLocation Location
    {
        get => new(Line, Column);
        set
        {
            Line = value.Line;
            Column = value.Column;
        }
    }

    public int Line { get; set; }

    public int Column { get; set; }

    /// <summary>Visual column, or -1 when it is not known.</summary>
    public int VisualColumn { get; set; }

    /// <summary>
    /// Which side of a wrap this position is on. Where a line wraps without a space, the end of one
    /// row and the start of the next share an offset and a visual column; true is the earlier row.
    /// Has no effect at any other position.
    /// </summary>
    public bool IsAtEndOfLine { get; set; }

    public override string ToString()
        => $"[TextViewPosition Line={Line} Column={Column} VisualColumn={VisualColumn} IsAtEndOfLine={IsAtEndOfLine}]";

    public override bool Equals(object? obj) => obj is TextViewPosition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Line, Column, VisualColumn, IsAtEndOfLine);

    public bool Equals(TextViewPosition other)
        => Line == other.Line
            && Column == other.Column
            && VisualColumn == other.VisualColumn
            && IsAtEndOfLine == other.IsAtEndOfLine;

    public static bool operator ==(TextViewPosition left, TextViewPosition right) => left.Equals(right);

    public static bool operator !=(TextViewPosition left, TextViewPosition right) => !left.Equals(right);

    /// <summary>Orders by location, then visual column, then the end of a wrap before its start.</summary>
    public int CompareTo(TextViewPosition other)
    {
        int result = Location.CompareTo(other.Location);
        if (result != 0)
        {
            return result;
        }
        result = VisualColumn.CompareTo(other.VisualColumn);
        if (result != 0)
        {
            return result;
        }
        if (IsAtEndOfLine == other.IsAtEndOfLine)
        {
            return 0;
        }
        return IsAtEndOfLine ? -1 : 1;
    }
}
