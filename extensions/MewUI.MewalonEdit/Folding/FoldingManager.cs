using Aprillz.MewUI.Text;

namespace ICSharpCode.AvalonEdit.Folding;

public sealed class FoldingManager
{
    private readonly TextEditor _editor;
    private readonly FoldingProjection _projection;
    private readonly List<FoldingSection> _foldings = [];
    private bool _uninstalled;

    private FoldingManager(TextEditor editor)
    {
        _editor = editor;
        _projection = new FoldingProjection(this);
        _editor.Surface.Extensions.Projections.Add(_projection);
        _editor.Surface.Extensions.LineCollapsers.Add(_projection);
        _editor.SurfaceChanged += OnSurfaceChanged;
    }

    public IEnumerable<FoldingSection> AllFoldings => _foldings;
    public event EventHandler? FoldingsChanged;

    public static FoldingManager Install(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new FoldingManager(editor);
    }

    public static FoldingManager Install(ICSharpCode.AvalonEdit.Editing.TextArea textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        return Install(textArea.Editor);
    }

    public static void Uninstall(FoldingManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (manager._uninstalled) return;
        manager._uninstalled = true;
        manager._editor.SurfaceChanged -= manager.OnSurfaceChanged;
        manager._editor.Surface.Extensions.Projections.Remove(manager._projection);
        manager._editor.Surface.Extensions.LineCollapsers.Remove(manager._projection);
        manager._editor.InvalidateTextView();
    }

    public void UpdateFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)
    {
        ObjectDisposedException.ThrowIf(_uninstalled, this);
        ArgumentNullException.ThrowIfNull(newFoldings);
        var ordered = newFoldings.OrderBy(static item => item.StartOffset).ThenBy(static item => item.EndOffset).ToArray();
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
            var existing = _foldings.FirstOrDefault(item =>
                item.StartOffset == folding.StartOffset && item.EndOffset == folding.EndOffset);
            if (existing is null)
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
        => _foldings.FirstOrDefault(item => item.StartOffset >= startOffset);

    internal IReadOnlyList<FoldingSection> Collapsed
        => _foldings.Where(static item => item.IsFolded).ToArray();

    internal void NotifyChanged()
    {
        if (_uninstalled) return;
        FoldingsChanged?.Invoke(this, EventArgs.Empty);
        _editor.InvalidateTextView();
    }

    private void OnSurfaceChanged(
        Aprillz.MewUI.Controls.NewMultiLineTextBox previous,
        Aprillz.MewUI.Controls.NewMultiLineTextBox current)
    {
        previous.Extensions.Projections.Remove(_projection);
        previous.Extensions.LineCollapsers.Remove(_projection);
        current.Extensions.Projections.Add(_projection);
        current.Extensions.LineCollapsers.Add(_projection);
        current.InvalidateTextView();
    }

    private sealed class FoldingProjection(FoldingManager manager) : ITextProjection, ITextLineCollapser
    {
        public bool IsCollapsed(LogicalTextLine line)
            => manager.Collapsed.Any(folding =>
                line.Offset > folding.StartOffset &&
                line.Offset + line.Length <= folding.EndOffset);

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
