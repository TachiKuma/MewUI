using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Document;

public sealed class DocumentLine
{
    internal DocumentLine(IReadOnlyDocumentLine source)
    {
        LineNumber = source.LineNumber + 1;
        Offset = source.Offset;
        Length = source.Length;
        TotalLength = source.TotalLength;
        DelimiterLength = source.TotalLength - source.Length;
    }

    public int LineNumber { get; }
    public int Offset { get; }
    public int Length { get; }
    public int TotalLength { get; }
    public int DelimiterLength { get; }
    public int EndOffset => Offset + Length;
}
