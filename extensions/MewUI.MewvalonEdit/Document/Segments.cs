namespace Aprillz.MewUI.MewvalonEdit.Document;

public interface ITextSource
{
    int TextLength { get; }
    char GetCharAt(int offset);
    string GetText(int offset, int length);
}

public interface ISegment
{
    int Offset { get; }
    int Length { get; }
    int EndOffset => Offset + Length;
}

public readonly record struct SimpleSegment(int Offset, int Length) : ISegment
{
    public int EndOffset => Offset + Length;
}

public readonly record struct TextLocation(int Line, int Column);

/// <summary>A replace that happened to a text source, with the text on both sides of it.</summary>
public class TextChangeEventArgs(int offset, string? removedText, string? insertedText) : EventArgs
{
    public int Offset { get; } = offset >= 0
        ? offset
        : throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must not be negative.");

    /// <summary>The text that was removed.</summary>
    public ITextSource RemovedText { get; } = new StringTextSource(removedText ?? string.Empty);

    /// <summary>The text that was inserted.</summary>
    public ITextSource InsertedText { get; } = new StringTextSource(insertedText ?? string.Empty);

    public int RemovalLength => RemovedText.TextLength;
    public int InsertionLength => InsertedText.TextLength;

    /// <summary>Where <paramref name="offset"/> lands after this change.</summary>
    public virtual int GetNewOffset(int offset, AnchorMovementType movementType = AnchorMovementType.Default)
    {
        if (offset >= Offset && offset <= Offset + RemovalLength)
        {
            return movementType == AnchorMovementType.BeforeInsertion
                ? Offset
                : Offset + InsertionLength;
        }
        return offset > Offset ? offset + InsertionLength - RemovalLength : offset;
    }
}

/// <summary>A replace that happened to a document.</summary>
public class DocumentChangeEventArgs(int offset, string? removedText, string? insertedText)
    : TextChangeEventArgs(offset, removedText, insertedText)
{
    private OffsetChangeMap? _offsetChangeMap;

    /// <summary>The change as an offset map, so an offset can be carried across it.</summary>
    public OffsetChangeMap OffsetChangeMap
        => _offsetChangeMap ??= OffsetChangeMap.FromSingleElement(
            new OffsetChangeMapEntry(Offset, RemovalLength, InsertionLength));

    public override int GetNewOffset(int offset, AnchorMovementType movementType = AnchorMovementType.Default)
        => OffsetChangeMap.GetNewOffset(offset, movementType);
}
