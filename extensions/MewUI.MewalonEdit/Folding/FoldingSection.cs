using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Folding;

public sealed class FoldingSection : ISegment
{
    private readonly FoldingManager _owner;
    private bool _isFolded;

    internal FoldingSection(FoldingManager owner, NewFolding folding)
    {
        _owner = owner;
        StartOffset = folding.StartOffset;
        EndOffset = folding.EndOffset;
        Title = folding.Name;
        IsDefinition = folding.IsDefinition;
        _isFolded = folding.DefaultClosed;
    }

    public int StartOffset { get; internal set; }
    public int EndOffset { get; internal set; }
    public string? Title { get; set; }
    public bool IsDefinition { get; internal set; }

    public bool IsFolded
    {
        get => _isFolded;
        set
        {
            if (_isFolded == value) return;
            _isFolded = value;
            _owner.NotifyChanged();
        }
    }

    int ISegment.Offset => StartOffset;
    int ISegment.Length => EndOffset - StartOffset;
}
