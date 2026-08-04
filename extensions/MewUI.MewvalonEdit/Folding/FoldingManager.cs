using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

public sealed class FoldingManager
{
    private readonly TextEditor _editor;
    private readonly FoldingProjection _projection;
    private readonly FoldingMargin _margin;
    private readonly List<FoldingSection> _foldings = [];
    private readonly List<FoldingSection> _collapsed = [];
    private readonly List<(int Start, int End)> _collapsedRanges = [];
    private bool _uninstalled;

    private FoldingManager(TextEditor editor)
    {
        _editor = editor;
        _projection = new FoldingProjection(this);
        _editor.Surface.Extensions.Projections.Add(_projection);
        _editor.Surface.Extensions.LineCollapsers.Add(_projection);
        _editor.BackgroundRenderers.Add(new FoldedSectionRenderer(this));
        _margin = new FoldingMargin { FoldingManager = this };
        _editor.TextArea.LeftMargins.Add(_margin);
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
        manager._editor.Surface.Extensions.Projections.Remove(manager._projection);
        manager._editor.Surface.Extensions.LineCollapsers.Remove(manager._projection);
        manager._editor.TextArea.LeftMargins.Remove(manager._margin);
        manager._margin.FoldingManager = null;
        manager._editor.InvalidateTextView();
    }

    public void UpdateFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)
    {
        ObjectDisposedException.ThrowIf(_uninstalled, this);
        ArgumentNullException.ThrowIfNull(newFoldings);
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
        _foldings.Clear();
        _foldings.AddRange(replacement);
        NotifyChanged();
    }

    public IEnumerable<FoldingSection> GetFoldingsContaining(int offset)
        => _foldings.Where(item => item.StartOffset <= offset && offset <= item.EndOffset);

    public FoldingSection? GetNextFolding(int startOffset)
    {
        int low = 0;
        int high = _foldings.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_foldings[middle].StartOffset < startOffset) low = middle + 1;
            else high = middle;
        }
        return low < _foldings.Count ? _foldings[low] : null;
    }

    internal IReadOnlyList<FoldingSection> Collapsed
        => _collapsed;

    internal void NotifyChanged()
    {
        if (_uninstalled) return;
        RebuildCollapsedIndex();
        FoldingsChanged?.Invoke(this, EventArgs.Empty);
        _editor.InvalidateTextView();
    }

    private void RebuildCollapsedIndex()
    {
        _collapsed.Clear();
        _collapsed.AddRange(_foldings.Where(static item => item.IsFolded));
        _collapsedRanges.Clear();
        foreach (var folding in _collapsed)
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

    private bool IsLineCollapsed(LogicalTextLine line)
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
        return line.Offset > range.Start && line.Offset + line.Length <= range.End;
    }

    /// <summary>
    /// Outlines the placeholder a collapsed section leaves behind. The projection turns the folded
    /// range into placeholder text, so the box is found by mapping the section's source range
    /// through the line's offset map rather than by measuring the text again.
    /// </summary>
    private sealed class FoldedSectionRenderer(FoldingManager manager) : Rendering.IBackgroundRenderer
    {
        // Under the glyphs, so the placeholder text stays readable on top of the box.
        public Rendering.KnownLayer Layer => Rendering.KnownLayer.Text;

        public void Draw(Rendering.TextView textView, IGraphicsContext context)
        {
            var folds = manager.Collapsed;
            if (folds.Count == 0)
            {
                return;
            }

            var host = textView.Host;
            var viewport = host.TextViewportBounds;
            var scroll = host.ScrollOffset;
            var color = manager._editor.FoldingMarkerColor;
            var bounds = new List<Rect>();
            foreach (var line in host.VisibleTextLines)
            {
                var logical = line.LogicalLine;
                foreach (var folding in folds)
                {
                    if (folding.StartOffset < logical.Offset ||
                        folding.StartOffset >= logical.Offset + logical.Length + 1)
                    {
                        continue;
                    }

                    int start = line.MapSourceOffsetToProjected(
                        Math.Clamp(folding.StartOffset - logical.Offset, 0, logical.Length));
                    int end = line.MapSourceOffsetToProjected(
                        Math.Clamp(folding.EndOffset - logical.Offset, 0, logical.Length));
                    if (end <= start)
                    {
                        continue;
                    }

                    bounds.Clear();
                    line.GetRangeBounds(new TextRange(start, end - start), bounds);
                    foreach (var rect in bounds)
                    {
                        var box = new Rect(
                            viewport.X + line.DocumentX + rect.X - scroll.X,
                            viewport.Y + line.DocumentY + rect.Y - scroll.Y,
                            rect.Width,
                            rect.Height).Deflate(new Thickness(0, 0.5, 0, 0.5));
                        context.DrawRoundedRectangle(box, 2, 2, color, 1);
                    }
                }
            }
        }
    }

    private sealed class FoldingProjection(FoldingManager manager) : ITextProjection, ITextLineCollapser
    {
        public bool IsCollapsed(LogicalTextLine line)
            => manager.IsLineCollapsed(line);

        public ProjectedText Project(in TextProjectionContext context)
        {
            var folds = manager.Collapsed;
            if (folds.Count == 0)
            {
                return new ProjectedText(context.SourceText, IdentityTextOffsetMap.Instance);
            }

            int lineStart = context.LogicalLine.Offset;
            int lineLength = context.SourceText.Length;
            int lineEnd = lineStart + lineLength;
            var overlapping = folds
                .Where(item => item.StartOffset < lineEnd && item.EndOffset > lineStart)
                .OrderBy(static item => item.StartOffset)
                .ToArray();
            if (overlapping.Length == 0)
            {
                return new ProjectedText(context.SourceText, IdentityTextOffsetMap.Instance);
            }

            string source = context.SourceText.ToString();
            var result = new System.Text.StringBuilder(source.Length);
            var boundaries = new List<int> { 0 };
            int cursor = 0;
            foreach (var folding in overlapping)
            {
                int foldStart = Math.Clamp(folding.StartOffset - lineStart, 0, lineLength);
                int foldEnd = Math.Clamp(folding.EndOffset - lineStart, 0, lineLength);
                if (foldEnd <= cursor) continue;
                AppendSource(source, cursor, Math.Max(cursor, foldStart), result, boundaries);
                if (folding.StartOffset >= lineStart && folding.StartOffset < lineEnd)
                {
                    string placeholder = string.IsNullOrEmpty(folding.Title) ? "…" : folding.Title!;
                    AppendPlaceholder(placeholder, foldStart, foldEnd, result, boundaries);
                }
                cursor = Math.Max(cursor, foldEnd);
            }
            AppendSource(source, cursor, lineLength, result, boundaries);
            return new ProjectedText(result.ToString().AsMemory(), new FoldingOffsetMap(boundaries.ToArray()));
        }

        private static void AppendSource(
            string source,
            int start,
            int end,
            System.Text.StringBuilder result,
            List<int> boundaries)
        {
            for (int index = start; index < end; index++)
            {
                result.Append(source[index]);
                boundaries.Add(index + 1);
            }
        }

        private static void AppendPlaceholder(
            string placeholder,
            int sourceStart,
            int sourceEnd,
            System.Text.StringBuilder result,
            List<int> boundaries)
        {
            for (int index = 0; index < placeholder.Length; index++)
            {
                result.Append(placeholder[index]);
                boundaries.Add(index == placeholder.Length - 1 ? sourceEnd : sourceStart);
            }
        }
    }

    private sealed class FoldingOffsetMap(int[] boundaries) : ITextOffsetMap
    {
        public int MapToSource(int projectedOffset)
            => boundaries[Math.Clamp(projectedOffset, 0, boundaries.Length - 1)];

        public int MapFromSource(int sourceOffset)
        {
            sourceOffset = Math.Max(0, sourceOffset);
            for (int index = 0; index < boundaries.Length; index++)
            {
                if (boundaries[index] >= sourceOffset) return index;
            }
            return boundaries.Length - 1;
        }
    }
}
