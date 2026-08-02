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

public sealed class DocumentChangeEventArgs(int offset, int removalLength, int insertionLength) : EventArgs
{
    public int Offset { get; } = offset;
    public int RemovalLength { get; } = removalLength;
    public int InsertionLength { get; } = insertionLength;
}
