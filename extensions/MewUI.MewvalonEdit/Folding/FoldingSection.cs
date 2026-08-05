using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

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

    /// <summary>Whether this section is a definition rather than a plain block, as the strategy classified it.</summary>
    public bool IsDefinition { get; set; }

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
