using System.Collections;
using System.Collections.ObjectModel;

namespace Aprillz.MewUI.MewvalonEdit.Document;

/// <summary>Segment whose offsets follow document edits while it lives in a <see cref="TextSegmentCollection{T}"/>.</summary>
public class TextSegment : ISegment
{
    public int StartOffset { get; set; }
    public int Length
    {
        get => EndOffset - StartOffset;
        set => EndOffset = StartOffset + Math.Max(0, value);
    }
    public int EndOffset { get; set; }

    int ISegment.Offset => StartOffset;
    int ISegment.Length => Length;

    public override string ToString() => $"[{StartOffset}..{EndOffset}]";
}

/// <summary>
/// Segments kept up to date across document edits. AvalonEdit backs this with an interval tree;
/// this port keeps a flat list, which is enough for the marker counts editors hold in practice.
/// </summary>
public sealed class TextSegmentCollection<T> : ICollection<T> where T : TextSegment
{
    private readonly List<T> _segments = [];
    private readonly TextDocument? _document;

    public TextSegmentCollection()
    {
    }

    public TextSegmentCollection(TextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Changed += OnDocumentChanged;
    }

    public int Count => _segments.Count;
    bool ICollection<T>.IsReadOnly => false;

    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _segments.Add(item);
    }

    public bool Remove(T item) => _segments.Remove(item);
    public void Clear() => _segments.Clear();
    public bool Contains(T item) => _segments.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _segments.CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator() => _segments.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Segments overlapping the given range, ordered by start offset.</summary>
    public ReadOnlyCollection<T> FindOverlappingSegments(int offset, int length)
    {
        int end = offset + length;
        return new ReadOnlyCollection<T>(_segments
            .Where(segment => segment.StartOffset < end && segment.EndOffset > offset)
            .OrderBy(static segment => segment.StartOffset)
            .ToList());
    }

    public ReadOnlyCollection<T> FindOverlappingSegments(ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return FindOverlappingSegments(segment.Offset, segment.Length);
    }

    /// <summary>Segments covering the given offset.</summary>
    public ReadOnlyCollection<T> FindSegmentsContaining(int offset)
        => new(_segments
            .Where(segment => segment.StartOffset <= offset && segment.EndOffset >= offset)
            .OrderBy(static segment => segment.StartOffset)
            .ToList());

    public T? FindFirstSegmentWithStartAfter(int offset)
        => _segments.Where(segment => segment.StartOffset >= offset)
            .OrderBy(static segment => segment.StartOffset)
            .FirstOrDefault();

    /// <summary>Shifts the stored offsets for an edit. Called automatically when built with a document.</summary>
    public void UpdateOffsets(DocumentChangeEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        UpdateOffsets(e.Offset, e.RemovalLength, e.InsertionLength);
    }

    private void UpdateOffsets(int offset, int removalLength, int insertionLength)
    {
        int delta = insertionLength - removalLength;
        int removalEnd = offset + removalLength;
        for (int index = _segments.Count - 1; index >= 0; index--)
        {
            var segment = _segments[index];
            segment.StartOffset = Shift(segment.StartOffset, offset, removalEnd, delta);
            segment.EndOffset = Shift(segment.EndOffset, offset, removalEnd, delta);
            if (segment.EndOffset < segment.StartOffset)
            {
                segment.EndOffset = segment.StartOffset;
            }
        }
    }

    private static int Shift(int position, int offset, int removalEnd, int delta)
    {
        if (position <= offset) return position;
        return position >= removalEnd ? position + delta : offset;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
        => UpdateOffsets(e.Offset, e.RemovalLength, e.InsertionLength);
}
