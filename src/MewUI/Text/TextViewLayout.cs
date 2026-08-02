namespace Aprillz.MewUI.Text;

public sealed class TextViewLayout : ITextViewLayout
{
    private const int VirtualWrapThreshold = 64 * 1024;
    private const int VirtualWrapSampleLength = 8 * 1024;
    private const int VirtualWrapOverscanRows = 3;
    private readonly ITextEngine _engine;
    private readonly IReadOnlyTextDocument _document;
    private readonly TextRunStyle _defaultStyle;
    private readonly TextParagraphStyle _paragraph;
    private readonly uint _dpi;
    private readonly TextViewExtensionPipeline _extensions;
    private readonly List<TextLineLayout> _materialized = [];
    private LineState[] _states;
    private double _estimatedLineHeight;
    private bool _disposed;

    public TextViewLayout(
        ITextEngine engine,
        IReadOnlyTextDocument document,
        TextRunStyle defaultStyle,
        TextParagraphStyle? paragraph = null,
        TextViewExtensionPipeline? extensions = null,
        uint dpi = 96)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(defaultStyle.FontFamily) || defaultStyle.FontSize <= 0)
        {
            throw new ArgumentException("A valid default text style is required.", nameof(defaultStyle));
        }

        _defaultStyle = defaultStyle;
        _paragraph = paragraph ?? new TextParagraphStyle { Wrapping = TextWrapping.Wrap };
        _extensions = extensions ?? new TextViewExtensionPipeline();
        _dpi = dpi == 0 ? 96 : dpi;
        _estimatedLineHeight = Math.Max(1, _paragraph.LineHeight ?? defaultStyle.FontSize * 1.25);
        _states = CreateStates(document.LineCount, _estimatedLineHeight);
        ApplyLineCollapsing();
    }

    public TextViewport Viewport { get; private set; }

    public IReadOnlyList<TextLineLayout> MaterializedLines => _materialized;

    public double ExtentHeight => _states.Sum(static state => state.Height);

    public void SetViewport(TextViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (double.IsNaN(viewport.Width) || double.IsNaN(viewport.Height) ||
            viewport.Width < 0 || viewport.Height < 0 ||
            viewport.HorizontalOffset < 0 || viewport.VerticalOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        Viewport = viewport;
        EnsureStateCount();
        MaterializeViewport();
    }

    public void Invalidate(TextChange change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (change.Offset < 0 || change.RemovedLength < 0 || change.InsertedLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        EnsureStateCount();
        int safeOffset = Math.Clamp(change.Offset, 0, _document.TextLength);
        int firstLine = _document.LineCount == 0
            ? 0
            : _document.GetLineByOffset(safeOffset).LineNumber;
        firstLine = Math.Clamp(firstLine, 0, Math.Max(0, _states.Length - 1));

        for (int i = firstLine; i < _states.Length; i++)
        {
            var state = _states[i];
            _engine.ManagedCache.ReleaseOwner(state.Owner);
            state.Layout = null;
            state.Version = -1;
            state.Height = _estimatedLineHeight;
            state.Virtual = null;
            state.SliceStart = -1;
            state.SliceLength = -1;
            state.Width = -1;
        }

        MaterializeViewport();
    }

    public TextViewHit HitTest(Point viewportPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_document.LineCount == 0)
        {
            return default;
        }

        double documentX = viewportPoint.X + Viewport.HorizontalOffset;
        double documentY = Math.Max(0, viewportPoint.Y + Viewport.VerticalOffset);
        int lineNumber = FindLineByY(documentY);
        double lineY = GetLineY(lineNumber);
        var layout = GetOrCreateLine(lineNumber, lineY, documentY);
        var lineHit = layout.HitTest(new Point(documentX, documentY - layout.DocumentY));
        int projectedInsertion = Math.Max(0, lineHit.InsertionIndex);
        int insertion = Math.Clamp(layout.MapProjectedOffsetToSource(projectedInsertion), 0, layout.LogicalLine.Length);
        int visualRow = 0;
        foreach (var visual in layout.VisualLines)
        {
            if (documentY < visual.Bounds.Bottom)
            {
                visualRow = visual.VisualRow;
                break;
            }
        }

        return new TextViewHit(
            layout.LogicalLine.Offset + insertion,
            lineNumber,
            visualRow,
            lineHit);
    }

    public Rect GetCaretBounds(int documentOffset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_document.LineCount == 0)
        {
            return Rect.Empty;
        }

        documentOffset = Math.Clamp(documentOffset, 0, _document.TextLength);
        var source = _document.GetLineByOffset(documentOffset);
        int visibleLineNumber = FindVisibleCaretLine(source.LineNumber);
        if (visibleLineNumber != source.LineNumber)
        {
            source = _document.GetLineByNumber(visibleLineNumber);
            documentOffset = source.Offset + source.Length;
        }
        double lineY = GetLineY(source.LineNumber);
        var layout = GetOrCreateLine(source.LineNumber, lineY, sourceOffset: documentOffset - source.Offset);
        int sourceOffset = Math.Clamp(documentOffset - layout.LogicalLine.Offset, 0, layout.LogicalLine.Length);
        int projectedOffset = layout.MapSourceOffsetToProjected(sourceOffset);
        var local = layout.GetCaretBounds(new CharacterHit(projectedOffset, 0));
        return new Rect(local.X, layout.DocumentY + local.Y, local.Width, local.Height);
    }

    private void MaterializeViewport()
    {
        _materialized.Clear();
        if (_states.Length == 0 || Viewport.Width <= 0 || Viewport.Height <= 0)
        {
            return;
        }

        int firstVisible = FindLineByY(Viewport.VerticalOffset);
        int first = Math.Max(0, firstVisible - 1);
        double y = GetLineY(first);
        double limit = Viewport.VerticalOffset + Viewport.Height + _estimatedLineHeight;

        for (int lineNumber = first; lineNumber < _states.Length && y <= limit; lineNumber++)
        {
            if (_states[lineNumber].Collapsed)
            {
                continue;
            }
            double targetY = Math.Max(y, Viewport.VerticalOffset - _estimatedLineHeight);
            var layout = GetOrCreateLine(lineNumber, y, targetY);
            _materialized.Add(layout);
            y += _states[lineNumber].Height;
        }
    }

    private TextLineLayout GetOrCreateLine(
        int lineNumber,
        double documentY,
        double? targetDocumentY = null,
        int? sourceOffset = null)
    {
        var state = _states[lineNumber];
        var source = _document.GetLineByNumber(lineNumber);
        if (ShouldVirtualize(source) &&
            (state.Virtual is null || state.Version != _document.Version || state.Width != Viewport.Width))
        {
            InitializeVirtualState(state, source);
        }

        int sliceStart = 0;
        int sliceLength = source.Length;
        int visualRowOffset = 0;
        double layoutY = documentY;
        if (state.Virtual is { } virtualState)
        {
            int targetRow = sourceOffset.HasValue
                ? virtualState.GetRowForOffset(sourceOffset.Value)
                : virtualState.GetRowForY(Math.Max(0, (targetDocumentY ?? documentY) - documentY));
            visualRowOffset = Math.Max(0, targetRow - VirtualWrapOverscanRows);
            sliceStart = virtualState.GetOffsetForRow(visualRowOffset);
            int requiredRows = Math.Max(1,
                (int)Math.Ceiling(Viewport.Height / virtualState.RowHeight) + VirtualWrapOverscanRows * 2 + 1);
            sliceLength = Math.Min(source.Length - sliceStart,
                Math.Max(VirtualWrapSampleLength, virtualState.GetLengthForRows(requiredRows)));
            NormalizeSliceBoundary(source, ref sliceStart, ref sliceLength);
            visualRowOffset = virtualState.GetRowForOffset(sliceStart);
            layoutY = documentY + visualRowOffset * virtualState.RowHeight;
        }

        if (state.Layout is not null && state.Version == _document.Version &&
            state.Width == Viewport.Width &&
            state.SliceStart == sliceStart && state.SliceLength == sliceLength)
        {
            state.Layout.SetDocumentY(layoutY);
            return state.Layout;
        }

        string sourceText = _document.GetText(source.Offset + sliceStart, sliceLength);
        var logical = new LogicalTextLine(
            source.LineNumber,
            source.Offset + sliceStart,
            sliceLength,
            sliceLength);
        ReadOnlyMemory<char> projectedMemory = sourceText.AsMemory();
        ITextOffsetMap offsetMap = IdentityTextOffsetMap.Instance;
        foreach (var projection in _extensions.Projections)
        {
            var projected = projection.Project(new TextProjectionContext(logical, projectedMemory));
            projectedMemory = projected.Text;
            var projectedMap = projected.OffsetMap ?? throw new InvalidOperationException("A projection must provide an offset map.");
            offsetMap = ReferenceEquals(offsetMap, IdentityTextOffsetMap.Instance)
                ? projectedMap
                : new ComposedTextOffsetMap(offsetMap, projectedMap);
        }

        string text = projectedMemory.ToString();
        var paintSpans = new List<TextPaintSpan>();
        var classificationContext = new TextClassificationContext(logical, text.AsMemory());
        foreach (var classifier in _extensions.Classifiers)
        {
            classifier.Classify(in classificationContext, paintSpans);
        }

        var geometryRuns = new List<GeometryStyleRun>();
        var inlines = new List<InlineRun>();
        var elementContext = new TextElementContext(logical, text.AsMemory());
        foreach (var generator in _extensions.ElementGenerators)
        {
            generator.Generate(in elementContext, inlines);
        }
        var transformContext = new TextLineTransformContext(logical, text.AsMemory(), _defaultStyle);
        foreach (var transformer in _extensions.Transformers)
        {
            transformer.Transform(in transformContext, geometryRuns, inlines);
        }

        var adornments = new List<ITextAdornment>();
        var adornmentContext = new TextAdornmentContext(logical, text.AsMemory());
        foreach (var provider in _extensions.AdornmentProviders)
        {
            provider.GetAdornments(in adornmentContext, adornments);
        }
        var paragraph = _paragraph with { MaxWidth = Viewport.Width };
        var request = new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = _dpi,
            Paragraph = paragraph,
            DefaultStyle = _defaultStyle,
            Runs = geometryRuns,
            Inlines = inlines,
            Revision = HashCode.Combine(_document.Version, _extensions.Revision, sliceStart, sliceLength)
        };
        var textLayout = _engine.GetOrCreateLayout(request, TextLayoutCachePolicy.Owner, state.Owner);
        var layout = new TextLineLayout(
            logical,
            textLayout,
            layoutY,
            offsetMap,
            paintSpans,
            adornments,
            visualRowOffset);

        state.Layout = layout;
        state.Version = _document.Version;
        state.Width = Viewport.Width;
        state.SliceStart = sliceStart;
        state.SliceLength = sliceLength;
        if (state.Virtual is { } activeVirtual)
        {
            activeVirtual.Refine(sliceLength, layout.VisualLines.Count, layout.Height);
            state.Height = activeVirtual.EstimatedHeight;
        }
        else
        {
            state.Height = Math.Max(1, layout.Height);
            _estimatedLineHeight = Math.Max(1, (_estimatedLineHeight * 7 + state.Height) / 8);
        }
        return layout;
    }

    private bool ShouldVirtualize(IReadOnlyDocumentLine source)
        => _paragraph.Wrapping == TextWrapping.Wrap &&
           source.Length >= VirtualWrapThreshold &&
           double.IsFinite(Viewport.Width) &&
           Viewport.Width > 0;

    private void InitializeVirtualState(LineState state, IReadOnlyDocumentLine source)
    {
        state.Virtual = null;
        state.Layout = null;
        state.SliceStart = -1;
        state.SliceLength = -1;
        int sampleLength = Math.Min(source.Length, VirtualWrapSampleLength);
        string sample = _document.GetText(source.Offset, sampleLength);
        var sampleLayout = _engine.CreateLayout(new TextLayoutRequest
        {
            Text = sample.AsMemory(),
            Dpi = _dpi,
            Paragraph = _paragraph with { MaxWidth = Viewport.Width },
            DefaultStyle = _defaultStyle,
            Revision = HashCode.Combine(_document.Version, source.LineNumber, sampleLength),
            Transient = true
        });
        int rows = Math.Max(1, sampleLayout.Lines.Count);
        double rowHeight = Math.Max(1, sampleLayout.ContentHeight / rows);
        state.Virtual = new VirtualWrapState(source.Length, sampleLength, rows, rowHeight);
        state.Height = state.Virtual.EstimatedHeight;
        state.Version = _document.Version;
        state.Width = Viewport.Width;
    }

    private void NormalizeSliceBoundary(IReadOnlyDocumentLine source, ref int start, ref int length)
    {
        if (start > 0 && char.IsLowSurrogate(_document.GetCharAt(source.Offset + start)))
        {
            start--;
            length++;
        }
        int end = Math.Min(source.Length, start + length);
        if (end < source.Length && end > start && char.IsHighSurrogate(_document.GetCharAt(source.Offset + end - 1)))
        {
            end++;
        }
        length = end - start;
    }

    private int FindLineByY(double documentY)
    {
        double y = 0;
        for (int i = 0; i < _states.Length; i++)
        {
            double next = y + _states[i].Height;
            if (documentY < next)
            {
                return i;
            }
            y = next;
        }
        return Math.Max(0, _states.Length - 1);
    }

    private double GetLineY(int lineNumber)
    {
        double y = 0;
        for (int i = 0; i < lineNumber; i++)
        {
            y += _states[i].Height;
        }
        return y;
    }

    private void EnsureStateCount()
    {
        int lineCount = Math.Max(0, _document.LineCount);
        if (_states.Length == lineCount)
        {
            return;
        }

        var replacement = CreateStates(lineCount, _estimatedLineHeight);
        int copy = Math.Min(_states.Length, replacement.Length);
        Array.Copy(_states, replacement, copy);
        for (int i = copy; i < replacement.Length; i++)
        {
            replacement[i] = new LineState(_estimatedLineHeight);
        }
        if (replacement.Length < _states.Length)
        {
            for (int i = replacement.Length; i < _states.Length; i++)
            {
                _engine.ManagedCache.ReleaseOwner(_states[i].Owner);
            }
        }
        _states = replacement;
        ApplyLineCollapsing();
    }

    private void ApplyLineCollapsing()
    {
        if (_extensions.LineCollapsers.Count == 0) return;
        for (int lineNumber = 0; lineNumber < _states.Length; lineNumber++)
        {
            var source = _document.GetLineByNumber(lineNumber);
            var logical = new LogicalTextLine(source.LineNumber, source.Offset, source.Length, source.TotalLength);
            bool collapsed = _extensions.LineCollapsers.Any(collapser => collapser.IsCollapsed(logical));
            var state = _states[lineNumber];
            state.Collapsed = collapsed;
            if (collapsed)
            {
                _engine.ManagedCache.ReleaseOwner(state.Owner);
                state.Layout = null;
                state.Height = 0;
            }
            else if (state.Height <= 0)
            {
                state.Height = _estimatedLineHeight;
            }
        }
    }

    private int FindVisibleCaretLine(int lineNumber)
    {
        if (!_states[lineNumber].Collapsed) return lineNumber;
        for (int candidate = lineNumber - 1; candidate >= 0; candidate--)
        {
            if (!_states[candidate].Collapsed) return candidate;
        }
        for (int candidate = lineNumber + 1; candidate < _states.Length; candidate++)
        {
            if (!_states[candidate].Collapsed) return candidate;
        }
        return lineNumber;
    }

    private static LineState[] CreateStates(int count, double estimate)
    {
        var states = new LineState[Math.Max(0, count)];
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = new LineState(estimate);
        }
        return states;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var state in _states)
        {
            _engine.ManagedCache.ReleaseOwner(state.Owner);
            state.Layout = null;
        }
        _materialized.Clear();
    }

    private sealed class LineState(double height)
    {
        public object Owner { get; } = new();
        public long Version { get; set; } = -1;
        public double Height { get; set; } = height;
        public TextLineLayout? Layout { get; set; }
        public VirtualWrapState? Virtual { get; set; }
        public int SliceStart { get; set; } = -1;
        public int SliceLength { get; set; } = -1;
        public double Width { get; set; } = -1;
        public bool Collapsed { get; set; }
    }

    private sealed class VirtualWrapState(int sourceLength, int sampleLength, int sampleRows, double rowHeight)
    {
        private double _charactersPerRow = Math.Max(1, (double)sampleLength / Math.Max(1, sampleRows));

        public double RowHeight { get; private set; } = rowHeight;
        public double EstimatedHeight
            => Math.Max(RowHeight, Math.Ceiling(sourceLength / _charactersPerRow) * RowHeight);

        public int GetRowForY(double y)
            => Math.Max(0, (int)Math.Floor(y / RowHeight));

        public int GetRowForOffset(int offset)
            => Math.Max(0, (int)Math.Floor(Math.Clamp(offset, 0, sourceLength) / _charactersPerRow));

        public int GetOffsetForRow(int row)
            => Math.Clamp((int)Math.Floor(Math.Max(0, row) * _charactersPerRow), 0, sourceLength);

        public int GetLengthForRows(int rows)
            => Math.Max(1, (int)Math.Ceiling(Math.Max(1, rows) * _charactersPerRow));

        public void Refine(int materializedLength, int rows, double height)
        {
            if (materializedLength <= 0 || rows <= 0)
            {
                return;
            }
            double observed = (double)materializedLength / rows;
            _charactersPerRow = Math.Max(1, _charactersPerRow * 0.75 + observed * 0.25);
            RowHeight = Math.Max(1, RowHeight * 0.75 + height / rows * 0.25);
        }
    }
}
