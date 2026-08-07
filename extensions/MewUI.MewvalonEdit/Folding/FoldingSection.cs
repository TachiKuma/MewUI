using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

/// <summary>
/// A section that can be folded. Its offsets follow document edits for as long as it belongs to a
/// <see cref="FoldingManager"/>.
/// </summary>
public sealed class FoldingSection : TextSegment
{
    private readonly FoldingManager _manager;
    private bool _isFolded;
    private string? _title;

    internal FoldingSection(FoldingManager manager, int startOffset, int endOffset)
    {
        _manager = manager;
        StartOffset = startOffset;
        Length = endOffset - startOffset;
    }

    public bool IsFolded
    {
        get => _isFolded;
        set
        {
            if (_isFolded == value) return;
            _isFolded = value;
            _manager.Redraw();
        }
    }

    /// <summary>Text shown in place of the section while it is folded.</summary>
    public string? Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            if (_isFolded)
            {
                _manager.Redraw();
            }
        }
    }

    /// <summary>The text this section covers.</summary>
    public string TextContent => _manager.Document.GetText(StartOffset, EndOffset - StartOffset);

    /// <summary>Whether this section is a definition rather than a plain block, as the strategy classified it.</summary>
    public bool IsDefinition { get; set; }

    /// <summary>Caller-owned object associated with this section.</summary>
    public object? Tag { get; set; }
}
