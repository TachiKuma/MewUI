using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Text;
using System.Collections.ObjectModel;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

/// <summary>
/// Hides the logical lines a fold element on an earlier line already covers, which the core
/// requires of an extension that makes a line reach past its own end.
/// </summary>
internal sealed class FoldedLineCollapser(FoldingManager manager) : ITextLineCollapser
{
    public bool IsCollapsed(LogicalTextLine line) => manager.IsLineCollapsed(line);
}

/// <summary>Holds the foldings of one editor. Install it with <see cref="Install(TextEditor)"/>.</summary>
public sealed class FoldingManager
{
    private readonly TextEditor _editor;
    private readonly FoldingElementGenerator _generator;
    private readonly FoldedLineCollapser _collapser;
    private readonly FoldingMargin _margin;
    private readonly TextSegmentCollection<FoldingSection> _foldings;
    private readonly List<(int Start, int End)> _collapsedRanges = [];
    private bool _isFirstUpdate = true;
    private bool _uninstalled;
    // The original's Redraw only marks a view dirty; ours rebuilds it, so a batch that touches every
    // section has to announce itself once rather than per section.
    private int _batchDepth;
    private bool _batchChanged;

    private FoldingManager(TextEditor editor)
    {
        _editor = editor;
        _foldings = new TextSegmentCollection<FoldingSection>(editor.Document);
        editor.Document.Changed += OnDocumentChanged;
        _generator = new FoldingElementGenerator(editor) { FoldingManager = this };
        _collapser = new FoldedLineCollapser(this);
        _editor.TextArea.TextView.ElementGenerators.Add(_generator);
        _editor.Surface.Extensions.LineCollapsers.Add(_collapser);
        _margin = new FoldingMargin { FoldingManager = this };
        _editor.TextArea.LeftMargins.Add(_margin);
        _editor.TextArea.TextView.Services.AddService(this);
        _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
    }

    internal TextDocument Document => _editor.Document;

    /// <summary>All foldings, ordered by start offset.</summary>
    public IEnumerable<FoldingSection> AllFoldings => _foldings;

    public event EventHandler? FoldingsChanged;

    public static FoldingManager Install(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new FoldingManager(editor);
    }

    public static FoldingManager Install(Editing.TextArea textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        return Install(textArea.Editor);
    }

    public static void Uninstall(FoldingManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (manager._uninstalled) return;
        manager._uninstalled = true;
        manager.Clear();
        manager._editor.Document.Changed -= manager.OnDocumentChanged;
        manager._editor.TextArea.Caret.PositionChanged -= manager.OnCaretPositionChanged;
        manager._editor.TextArea.TextView.ElementGenerators.Remove(manager._generator);
        manager._editor.Surface.Extensions.LineCollapsers.Remove(manager._collapser);
        manager._editor.TextArea.LeftMargins.Remove(manager._margin);
        manager._editor.TextArea.TextView.Services.RemoveService<FoldingManager>();
        manager._margin.FoldingManager = null;
        manager._editor.InvalidateTextView();
    }

    /// <summary>Creates a folding over the given range.</summary>
    public FoldingSection CreateFolding(int startOffset, int endOffset)
    {
        if (startOffset >= endOffset)
        {
            throw new ArgumentException("startOffset must be less than endOffset", nameof(startOffset));
        }
        if (startOffset < 0 || endOffset > _editor.Document.TextLength)
        {
            throw new ArgumentException("Folding must be within document boundary", nameof(startOffset));
        }
        var section = new FoldingSection(this, startOffset, endOffset);
        _foldings.Add(section);
        Redraw();
        return section;
    }

    public void RemoveFolding(FoldingSection folding)
    {
        ArgumentNullException.ThrowIfNull(folding);
        folding.IsFolded = false;
        _foldings.Remove(folding);
        Redraw();
    }

    public void Clear()
    {
        _batchDepth++;
        try
        {
            foreach (var section in _foldings)
            {
                section.IsFolded = false;
            }
            _foldings.Clear();
            _batchChanged = true;
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>
    /// Replaces the current foldings with <paramref name="newFoldings"/>, keeping the folded state
    /// of the sections that survive.
    /// </summary>
    /// <param name="newFoldings">Sections the strategy found, sorted by start offset.</param>
    /// <param name="firstErrorOffset">
    /// Offset the parser stopped understanding the document at, or a negative value when it parsed
    /// the whole document. Existing foldings starting at or after it are kept even when they are
    /// absent from <paramref name="newFoldings"/>.
    /// </param>
    public void UpdateFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)
    {
        ObjectDisposedException.ThrowIf(_uninstalled, this);
        ArgumentNullException.ThrowIfNull(newFoldings);
        if (firstErrorOffset < 0)
        {
            firstErrorOffset = int.MaxValue;
        }

        _batchDepth++;
        try
        {
            MergeFoldings(newFoldings, firstErrorOffset);
        }
        finally
        {
            EndBatch();
        }
    }

    private void MergeFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)
    {
        var oldFoldings = _foldings.ToArray();
        int oldFoldingIndex = 0;
        int previousStartOffset = 0;
        // Both lists run in start-offset order, so one walk over the two keeps the sections that
        // still exist, and with them whatever the reader folded.
        foreach (var newFolding in newFoldings)
        {
            if (newFolding.StartOffset < previousStartOffset)
            {
                throw new ArgumentException("newFoldings must be sorted by start offset", nameof(newFoldings));
            }
            previousStartOffset = newFolding.StartOffset;
            if (newFolding.StartOffset == newFolding.EndOffset)
            {
                continue;
            }

            while (oldFoldingIndex < oldFoldings.Length &&
                   newFolding.StartOffset > oldFoldings[oldFoldingIndex].StartOffset)
            {
                RemoveFolding(oldFoldings[oldFoldingIndex++]);
            }

            FoldingSection section;
            if (oldFoldingIndex < oldFoldings.Length &&
                newFolding.StartOffset == oldFoldings[oldFoldingIndex].StartOffset)
            {
                // Matched on the start alone: an end that moved is the same section grown or shrunk,
                // and folding it again because the block gained a line would fight the reader.
                section = oldFoldings[oldFoldingIndex++];
                section.Length = newFolding.EndOffset - newFolding.StartOffset;
            }
            else
            {
                section = CreateFolding(newFolding.StartOffset, newFolding.EndOffset);
                // Only while opening the document: a region added later must not close under the
                // reader because the strategy declared it closed by default.
                if (_isFirstUpdate)
                {
                    section.IsFolded = newFolding.DefaultClosed;
                }
                section.Tag = newFolding;
            }
            section.Title = newFolding.Name;
            section.IsDefinition = newFolding.IsDefinition;
        }
        _isFirstUpdate = false;

        while (oldFoldingIndex < oldFoldings.Length)
        {
            var oldSection = oldFoldings[oldFoldingIndex++];
            if (oldSection.StartOffset >= firstErrorOffset)
            {
                break;
            }
            RemoveFolding(oldSection);
        }
        _batchChanged = true;
    }

    /// <summary>All foldings containing <paramref name="offset"/>.</summary>
    public ReadOnlyCollection<FoldingSection> GetFoldingsContaining(int offset)
        => _foldings.FindSegmentsContaining(offset);

    /// <summary>All foldings starting exactly at <paramref name="startOffset"/>.</summary>
    public ReadOnlyCollection<FoldingSection> GetFoldingsAt(int startOffset)
    {
        var result = new List<FoldingSection>();
        for (int index = _foldings.FindFirstIndexWithStartAfter(startOffset);
             index < _foldings.Count && _foldings[index].StartOffset == startOffset;
             index++)
        {
            result.Add(_foldings[index]);
        }
        return result.AsReadOnly();
    }

    /// <summary>First folding starting at or after <paramref name="startOffset"/>, or null.</summary>
    public FoldingSection? GetNextFolding(int startOffset)
        => _foldings.FindFirstSegmentWithStartAfter(startOffset);

    /// <summary>
    /// First offset at or after <paramref name="startOffset"/> where a folded section starts, or -1
    /// when none does.
    /// </summary>
    public int GetNextFoldedFoldingStart(int startOffset)
    {
        for (int index = _foldings.FindFirstIndexWithStartAfter(startOffset); index < _foldings.Count; index++)
        {
            if (_foldings[index].IsFolded)
            {
                return _foldings[index].StartOffset;
            }
        }
        return -1;
    }

    internal void Redraw()
    {
        if (_uninstalled) return;
        if (_batchDepth > 0)
        {
            _batchChanged = true;
            return;
        }
        RebuildCollapsedIndex();
        FoldingsChanged?.Invoke(this, EventArgs.Empty);
        _editor.InvalidateTextView();
    }

    private void EndBatch()
    {
        _batchDepth--;
        if (_batchDepth > 0 || !_batchChanged) return;
        _batchChanged = false;
        Redraw();
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        // The collection has already shifted the offsets. What an edit can still leave behind is a
        // section the text no longer has, which the reader would otherwise never be able to unfold.
        int newEndOffset = e.Offset + e.InsertionLength;
        var endLine = _editor.Document.GetLineByOffset(Math.Clamp(newEndOffset, 0, _editor.Document.TextLength));
        newEndOffset = endLine.Offset + endLine.TotalLength;
        _batchDepth++;
        try
        {
            foreach (var affected in _foldings.FindOverlappingSegments(e.Offset, newEndOffset - e.Offset).ToArray())
            {
                if (affected.Length == 0)
                {
                    RemoveFolding(affected);
                }
            }
        }
        finally
        {
            EndBatch();
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        int caretOffset = _editor.CaretOffset;
        foreach (var section in GetFoldingsContaining(caretOffset))
        {
            if (section.IsFolded && section.StartOffset < caretOffset && caretOffset < section.EndOffset)
            {
                section.IsFolded = false;
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
            int middle = low + ((high - low) / 2);
            if (_collapsedRanges[middle].Start < line.Offset) low = middle + 1;
            else high = middle;
        }
        if (low == 0) return false;
        var range = _collapsedRanges[low - 1];
        return line.Offset > range.Start && line.Offset <= range.End;
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
}
