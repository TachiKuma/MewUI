using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

/// <summary>
/// Hides the logical lines a fold element on an earlier line already covers, which the core
/// requires of an extension that makes a line reach past its own end.
/// </summary>
internal sealed class FoldedLineCollapser(FoldingManager manager) : ITextLineCollapser
{
    public bool IsCollapsed(LogicalTextLine line) => manager.IsLineCollapsed(line);
}

public sealed class FoldingManager
{
    private readonly TextEditor _editor;
    private readonly FoldingElementGenerator _generator;
    private readonly FoldedLineCollapser _collapser;
    private readonly FoldingMargin _margin;
    private readonly List<FoldingSection> _foldings = [];
    private readonly List<(int Start, int End)> _collapsedRanges = [];
    private bool _uninstalled;

    private FoldingManager(TextEditor editor)
    {
        _editor = editor;
        _generator = new FoldingElementGenerator(editor) { FoldingManager = this };
        _collapser = new FoldedLineCollapser(this);
        _editor.TextArea.TextView.ElementGenerators.Add(_generator);
        _editor.Surface.Extensions.LineCollapsers.Add(_collapser);
        _margin = new FoldingMargin { FoldingManager = this };
        _editor.TextArea.LeftMargins.Add(_margin);
        _editor.TextArea.TextView.Services.AddService(this);
    }

    public IEnumerable<FoldingSection> AllFoldings => _foldings;
    public event EventHandler? FoldingsChanged;

    public static FoldingManager Install(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new FoldingManager(editor);
    }

    public static FoldingManager Install(Aprillz.MewUI.MewvalonEdit.Editing.TextArea textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        return Install(textArea.Editor);
    }

    public static void Uninstall(FoldingManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (manager._uninstalled) return;
        manager._uninstalled = true;
        manager._editor.TextArea.TextView.ElementGenerators.Remove(manager._generator);
        manager._editor.Surface.Extensions.LineCollapsers.Remove(manager._collapser);
        manager._editor.TextArea.LeftMargins.Remove(manager._margin);
        manager._editor.TextArea.TextView.Services.RemoveService<FoldingManager>();
        manager._margin.FoldingManager = null;
        manager._editor.InvalidateTextView();
    }

    /// <summary>Replaces the current foldings, keeping the folded state of sections that survive.</summary>
    /// <param name="newFoldings">Sections the strategy found, sorted by start offset.</param>
    /// <param name="firstErrorOffset">
    /// Offset the parser stopped understanding the document at, or a negative value when it parsed
    /// the whole document. Foldings at or after it are carried over from the current set.
    /// </param>
    public void UpdateFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)
    {
        ObjectDisposedException.ThrowIf(_uninstalled, this);
        ArgumentNullException.ThrowIfNull(newFoldings);
        // Past the error the new list carries no information, so dropping those foldings would
        // collapse regions the parser simply could not reach.
        int keepFrom = firstErrorOffset < 0 ? int.MaxValue : firstErrorOffset;
        var ordered = newFoldings.OrderBy(static item => item.StartOffset).ThenBy(static item => item.EndOffset).ToArray();
        var existingByRange = new Dictionary<(int Start, int End), FoldingSection>(_foldings.Count);
        foreach (var existing in _foldings)
        {
            existingByRange.TryAdd((existing.StartOffset, existing.EndOffset), existing);
        }
        int previousStart = -1;
        var replacement = new List<FoldingSection>(ordered.Length);
        foreach (var folding in ordered)
        {
            if (folding.StartOffset < 0 || folding.EndOffset < folding.StartOffset ||
                folding.EndOffset > _editor.Document.TextLength)
            {
                throw new ArgumentOutOfRangeException(nameof(newFoldings), "Folding offsets must be inside the document.");
            }
            if (folding.StartOffset < previousStart)
            {
                throw new ArgumentException("Foldings must be sorted by start offset.", nameof(newFoldings));
            }
            previousStart = folding.StartOffset;
            if (!existingByRange.TryGetValue((folding.StartOffset, folding.EndOffset), out var existing))
            {
                existing = new FoldingSection(this, folding);
            }
            else
            {
                existing.Title = folding.Name;
                existing.IsDefinition = folding.IsDefinition;
            }
            replacement.Add(existing);
        }
        if (keepFrom != int.MaxValue)
        {
            var claimed = new HashSet<(int Start, int End)>(
                replacement.Select(static item => (item.StartOffset, item.EndOffset)));
            foreach (var existing in _foldings)
            {
                if (existing.StartOffset >= keepFrom && claimed.Add((existing.StartOffset, existing.EndOffset)))
                {
                    replacement.Add(existing);
                }
            }
            // GetNextFolding binary-searches this list, so the carried-over sections must land in order.
            replacement.Sort(static (left, right) => left.StartOffset != right.StartOffset
                ? left.StartOffset.CompareTo(right.StartOffset)
                : left.EndOffset.CompareTo(right.EndOffset));
        }
        _foldings.Clear();
        _foldings.AddRange(replacement);
        NotifyChanged();
    }

    public System.Collections.ObjectModel.ReadOnlyCollection<FoldingSection> GetFoldingsContaining(int offset)
        => _foldings.Where(item => item.StartOffset <= offset && offset <= item.EndOffset)
            .ToList().AsReadOnly();

    public FoldingSection? GetNextFolding(int startOffset)
    {
        int index = FindFirstFoldingWithStartAfter(startOffset);
        return index < _foldings.Count ? _foldings[index] : null;
    }

    /// <summary>
    /// First offset at or after <paramref name="startOffset"/> where a folded section starts, or -1
    /// when none does.
    /// </summary>
    public int GetNextFoldedFoldingStart(int startOffset)
    {
        for (int index = FindFirstFoldingWithStartAfter(startOffset); index < _foldings.Count; index++)
        {
            if (_foldings[index].IsFolded)
            {
                return _foldings[index].StartOffset;
            }
        }
        return -1;
    }

    private int FindFirstFoldingWithStartAfter(int startOffset)
    {
        int low = 0;
        int high = _foldings.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_foldings[middle].StartOffset < startOffset) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    internal void NotifyChanged()
    {
        if (_uninstalled) return;
        RebuildCollapsedIndex();
        FoldingsChanged?.Invoke(this, EventArgs.Empty);
        _editor.InvalidateTextView();
    }

    private void RebuildCollapsedIndex()
    {
        _collapsedRanges.Clear();
        foreach (var folding in _foldings.Where(static item => item.IsFolded))
        {
            if (_collapsedRanges.Count > 0 && folding.StartOffset <= _collapsedRanges[^1].End)
            {
                var current = _collapsedRanges[^1];
                _collapsedRanges[^1] = (current.Start, Math.Max(current.End, folding.EndOffset));
            }
            else
            {
                _collapsedRanges.Add((folding.StartOffset, folding.EndOffset));
            }
        }
    }

    /// <summary>
    /// Whether the fold element on an earlier line already covers this one. The element reaches to
    /// the end of the logical line the fold ends on, so that line is swallowed too even when the
    /// fold stops in the middle of it.
    /// </summary>
    internal bool IsLineCollapsed(LogicalTextLine line)
    {
        int low = 0;
        int high = _collapsedRanges.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_collapsedRanges[middle].Start < line.Offset) low = middle + 1;
            else high = middle;
        }
        if (low == 0) return false;
        var range = _collapsedRanges[low - 1];
        return line.Offset > range.Start && line.Offset <= range.End;
    }
}