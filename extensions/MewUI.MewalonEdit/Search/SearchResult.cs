using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Search;

public readonly record struct SearchResult(int Offset, int Length) : ISegment
{
    public int EndOffset => Offset + Length;
}
