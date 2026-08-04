namespace Aprillz.MewUI.Text;

public sealed class TextViewLayout : ITextViewLayout
{
    private const int VirtualLineThreshold = 64 * 1024;
    private const int VirtualWrapSampleLength = 8 * 1024;
    private const int VirtualSliceMinimumLength = 512;
    private const int VirtualWrapOverscanRows = 3;
    private const int VirtualNoWrapOverscanCharacters = 128;
    private readonly ITextEngine _engine;
    private readonly IReadOnlyTextDocument _document;
    private readonly TextRunStyle _defaultStyle;
    private readonly TextParagraphStyle _paragraph;
    private readonly uint _dpi;
    private readonly TextViewExtensionPipeline _extensions;
    private readonly List<TextLineLayout> _materialized = [];
    private LineState[] _states;
    private readonly LineMetricsIndex _metrics;
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
        _metrics = new LineMetricsIndex(_states);
        ApplyLineCollapsing();
    }

    public TextViewport Viewport { get; private set; }

    public IReadOnlyList<TextLineLayout> MaterializedLines => _materialized;

    /// <summary>Raised before the visible lines are built, carrying the first line number.</summary>
    public event Action<TextViewLayout, int>? LineConstructionStarting;

    /// <summary>Raised after the visible lines were built.</summary>
    public event Action<TextViewLayout>? LinesChanged;

    private bool _materializing;
    private (int Offset, int Length)? _pendingInvalidation;

    private static (int Offset, int Length) Merge((int Offset, int Length) pending, int offset, int length)
    {
        int start = Math.Min(pending.Offset, offset);
        int end = Math.Max(pending.Offset + pending.Length, offset + length);
        return (start, end - start);
    }

    public double ExtentWidth => _metrics.MaxWidth;

    public double ExtentHeight => _metrics.TotalHeight;

    /// <summary>Height of a line holding one character in the default style, independent of content.</summary>
    public double DefaultLineHeight => EnsureDefaultMetrics().Height;

    /// <summary>Baseline of a line holding one character in the default style.</summary>
    public double DefaultBaseline => EnsureDefaultMetrics().Baseline;

    /// <summary>Line number whose row contains <paramref name="documentY"/>.</summary>
    public int GetLineNumberByVisualTop(double documentY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _document.LineCount == 0 ? 0 : FindLineByY(documentY);
    }

    /// <summary>Document-space top of <paramref name="lineNumber"/>.</summary>
    public double GetVisualTopByLineNumber(int lineNumber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetLineY(Math.Clamp(lineNumber, 0, Math.Max(0, _states.Length - 1)));
    }

    private (double Height, double Baseline)? _defaultMetrics;

    private (double Height, double Baseline) EnsureDefaultMetrics()
    {
        if (_defaultMetrics is { } cached)
        {
            return cached;
        }

        // A single character rather than the document: the value has to stay put when the text does
        // not, and it is what margins align their rows against.
        var layout = _engine.CreateLayout(new TextLayoutRequest
        {
            Text = "x".AsMemory(),
            Dpi = _dpi,
            DefaultStyle = _defaultStyle,
            Paragraph = _paragraph with { MaxWidth = double.PositiveInfinity, Wrapping = TextWrapping.NoWrap },
        });
        double height = Math.Max(1, layout.ContentHeight);
        double baseline = layout.Lines.Count == 0 ? height : layout.Lines[0].Baseline;
        _defaultMetrics = (height, baseline);
        return _defaultMetrics.Value;
    }

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

        bool lineCountChanged = _states.Length != _document.LineCount;
        EnsureStateCount();
        int safeOffset = Math.Clamp(change.Offset, 0, _document.TextLength);
        int firstLine = _document.LineCount == 0
            ? 0
            : _document.GetLineByOffset(safeOffset).LineNumber;
        firstLine = Math.Clamp(firstLine, 0, Math.Max(0, _states.Length - 1));

        int lastLine = lineCountChanged
            ? _states.Length - 1
            : _document.GetLineByOffset(Math.Clamp(
                change.Offset + change.InsertedLength,
                0,
                _document.TextLength)).LineNumber;
        DirtyLines(firstLine, lastLine);
        MaterializeViewport();
    }

    /// <summary>
    /// Drops the cached layout of the lines overlapping the range. Unlike <see cref="Invalidate"/>
    /// the text is unchanged, so lines outside the range keep their layout.
    /// </summary>
    public void InvalidateRange(int offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        if (_document.LineCount == 0)
        {
            return;
        }
        if (_materializing)
        {
            // Called from a classifier while its line is being built. Rebuilding here would mutate
            // the loop it was called from, so the widest pending range runs once the loop ends.
            _pendingInvalidation = _pendingInvalidation is { } pending
                ? Merge(pending, offset, length)
                : (offset, length);
            return;
        }

        EnsureStateCount();
        int start = Math.Clamp(offset, 0, _document.TextLength);
        int end = Math.Clamp(offset + length, start, _document.TextLength);
        int firstLine = Math.Clamp(
            _document.GetLineByOffset(start).LineNumber, 0, Math.Max(0, _states.Length - 1));
        int lastLine = Math.Clamp(
            _document.GetLineByOffset(end).LineNumber, firstLine, Math.Max(0, _states.Length - 1));

        DirtyLines(firstLine, lastLine);
        MaterializeViewport();
    }

    private void DirtyLines(int firstLine, int lastLine)
    {
        for (int i = firstLine; i <= lastLine; i++)
        {
            var state = _states[i];
            _engine.ManagedCache.ReleaseOwner(state.Owner);
            state.Layout = null;
            state.Dirty = true;
            state.Virtual = null;
            state.VirtualNoWrap = null;
            state.SliceStart = -1;
            state.SliceLength = -1;
            state.Width = -1;
            SetStateMetrics(i, _estimatedLineHeight, 0);
        }
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
        var lineHit = layout.HitTestDocument(new Point(documentX, documentY - layout.DocumentY));
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
        var local = layout.GetDocumentCaretBounds(new CharacterHit(projectedOffset, 0));
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

        _materializing = true;
        try
        {
            LineConstructionStarting?.Invoke(this, first);
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
        finally
        {
            _materializing = false;
        }

        LinesChanged?.Invoke(this);

        if (_pendingInvalidation is { } pending)
        {
            // A classifier that invalidated while its own line was being built asked for a rebuild
            // it must not perform from inside the loop.
            _pendingInvalidation = null;
            InvalidateRange(pending.Offset, pending.Length);
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
        if (ShouldVirtualizeWrap(source) &&
            (state.Virtual is null || state.Dirty || state.SourceLength != source.Length || state.Width != Viewport.Width))
        {
            InitializeVirtualWrapState(lineNumber, state, source);
        }
        else if (ShouldVirtualizeNoWrap(source) &&
                 (state.VirtualNoWrap is null || state.Dirty || state.SourceLength != source.Length))
        {
            InitializeVirtualNoWrapState(lineNumber, state, source);
        }

        int sliceStart = 0;
        int sliceLength = source.Length;
        int visualRowOffset = 0;
        double layoutX = 0;
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
                Math.Max(VirtualSliceMinimumLength, virtualState.GetLengthForRows(requiredRows)));
            NormalizeSliceBoundary(source, ref sliceStart, ref sliceLength);
            visualRowOffset = virtualState.GetRowForOffset(sliceStart);
            layoutY = documentY + visualRowOffset * virtualState.RowHeight;
        }
        else if (state.VirtualNoWrap is { } noWrapState)
        {
            int targetOffset = sourceOffset.HasValue
                ? Math.Clamp(sourceOffset.Value, 0, source.Length)
                : noWrapState.GetOffsetForX(Viewport.HorizontalOffset);
            int requiredCharacters = Math.Max(
                VirtualSliceMinimumLength,
                noWrapState.GetLengthForWidth(Viewport.Width) + VirtualNoWrapOverscanCharacters * 2);
            sliceStart = Math.Clamp(
                targetOffset - VirtualNoWrapOverscanCharacters,
                0,
                Math.Max(0, source.Length - requiredCharacters));
            sliceLength = Math.Min(source.Length - sliceStart, requiredCharacters);
            NormalizeSliceBoundary(source, ref sliceStart, ref sliceLength);
            layoutX = noWrapState.GetXForOffset(sliceStart);
        }

        if (state.Layout is not null && !state.Dirty &&
            state.SourceOffset == source.Offset && state.SourceLength == source.Length &&
            state.Width == Viewport.Width &&
            state.SliceStart == sliceStart && state.SliceLength == sliceLength)
        {
            state.Layout.SetDocumentPosition(layoutX, layoutY);
            return state.Layout;
        }

        if (state.Layout is not null)
        {
            _engine.ManagedCache.ReleaseOwner(state.Owner);
            state.Layout = null;
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
        var classificationContext = new TextClassificationContext(logical, text.AsMemory(), offsetMap);
        foreach (var classifier in _extensions.Classifiers)
        {
            classifier.Classify(in classificationContext, paintSpans);
        }

        var geometryRuns = new List<GeometryStyleRun>();
        var inlines = new List<InlineRun>();
        var elementContext = new TextElementContext(logical, text.AsMemory(), offsetMap);
        foreach (var generator in _extensions.ElementGenerators)
        {
            generator.Generate(in elementContext, inlines);
        }
        var transformContext = new TextLineTransformContext(logical, text.AsMemory(), _defaultStyle, offsetMap);
        foreach (var transformer in _extensions.Transformers)
        {
            transformer.Transform(in transformContext, geometryRuns, inlines);
        }

        var adornments = new List<ITextAdornment>();
        var adornmentContext = new TextAdornmentContext(logical, text.AsMemory(), offsetMap);
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
            layoutX,
            layoutY,
            offsetMap,
            paintSpans,
            adornments,
            visualRowOffset);

        state.Layout = layout;
        state.Version = _document.Version;
        state.Dirty = false;
        state.SourceOffset = source.Offset;
        state.SourceLength = source.Length;
        state.Width = Viewport.Width;
        state.SliceStart = sliceStart;
        state.SliceLength = sliceLength;
        if (state.Virtual is { } activeVirtual)
        {
            activeVirtual.Refine(sliceLength, layout.VisualLines.Count, layout.Height);
            SetStateMetrics(
                lineNumber,
                activeVirtual.EstimatedHeight,
                Math.Max(state.ExtentWidth,
                    layout.VisualLines.Select(static line => line.Bounds.Right).DefaultIfEmpty(0).Max()));
        }
        else if (state.VirtualNoWrap is { } activeNoWrap)
        {
            activeNoWrap.Refine(sliceLength, textLayout.MeasuredSize.Width);
            SetStateMetrics(lineNumber, Math.Max(1, textLayout.ContentHeight), activeNoWrap.EstimatedWidth);
        }
        else
        {
            SetStateMetrics(
                lineNumber,
                Math.Max(1, layout.Height),
                layout.VisualLines.Select(static line => line.Bounds.Right).DefaultIfEmpty(0).Max());
            _estimatedLineHeight = Math.Max(1, (_estimatedLineHeight * 7 + state.Height) / 8);
        }
        return layout;
    }

    private bool ShouldVirtualizeWrap(IReadOnlyDocumentLine source)
        => _paragraph.Wrapping == TextWrapping.Wrap &&
           source.Length >= VirtualLineThreshold &&
           double.IsFinite(Viewport.Width) &&
           Viewport.Width > 0;

    private bool ShouldVirtualizeNoWrap(IReadOnlyDocumentLine source)
        => _paragraph.Wrapping == TextWrapping.NoWrap &&
           source.Length >= VirtualLineThreshold &&
           double.IsFinite(Viewport.Width) &&
           Viewport.Width > 0;

    private void InitializeVirtualWrapState(int lineNumber, LineState state, IReadOnlyDocumentLine source)
    {
        state.Virtual = null;
        state.VirtualNoWrap = null;
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
        SetStateMetrics(lineNumber, state.Virtual.EstimatedHeight, Viewport.Width);
        state.Version = _document.Version;
        state.SourceOffset = source.Offset;
        state.SourceLength = source.Length;
        state.Width = Viewport.Width;
    }

    private void InitializeVirtualNoWrapState(int lineNumber, LineState state, IReadOnlyDocumentLine source)
    {
        state.Virtual = null;
        state.VirtualNoWrap = null;
        state.Layout = null;
        state.SliceStart = -1;
        state.SliceLength = -1;
        int sampleLength = Math.Min(source.Length, VirtualWrapSampleLength);
        string sample = _document.GetText(source.Offset, sampleLength);
        var sampleLayout = _engine.CreateLayout(new TextLayoutRequest
        {
            Text = sample.AsMemory(),
            Dpi = _dpi,
            Paragraph = _paragraph with { MaxWidth = double.PositiveInfinity },
            DefaultStyle = _defaultStyle,
            Revision = HashCode.Combine(_document.Version, source.LineNumber, sampleLength),
            Transient = true
        });
        double averageWidth = sampleLength == 0
            ? Math.Max(1, _defaultStyle.FontSize * 0.5)
            : Math.Max(0.01, sampleLayout.MeasuredSize.Width / sampleLength);
        double rowHeight = Math.Max(1, sampleLayout.ContentHeight);
        state.VirtualNoWrap = new VirtualNoWrapState(source.Length, averageWidth);
        SetStateMetrics(lineNumber, rowHeight, state.VirtualNoWrap.EstimatedWidth);
        state.Version = _document.Version;
        state.SourceOffset = source.Offset;
        state.SourceLength = source.Length;
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
        => _metrics.FindLineByY(documentY);

    private double GetLineY(int lineNumber)
        => _metrics.GetLineY(lineNumber);

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
        _metrics.Reset(_states);
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
                SetStateMetrics(lineNumber, 0, 0);
            }
            else if (state.Height <= 0)
            {
                SetStateMetrics(lineNumber, _estimatedLineHeight, state.ExtentWidth);
            }
        }
    }

    private void SetStateMetrics(int lineNumber, double height, double width)
    {
        var state = _states[lineNumber];
        state.Height = height;
        state.ExtentWidth = width;
        _metrics.Update(lineNumber, height, width);
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
        public bool Dirty { get; set; } = true;
        public int SourceOffset { get; set; } = -1;
        public int SourceLength { get; set; } = -1;
        public double Height { get; set; } = height;
        public TextLineLayout? Layout { get; set; }
        public VirtualWrapState? Virtual { get; set; }
        public VirtualNoWrapState? VirtualNoWrap { get; set; }
        public int SliceStart { get; set; } = -1;
        public int SliceLength { get; set; } = -1;
        public double Width { get; set; } = -1;
        public double ExtentWidth { get; set; }
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

    private sealed class VirtualNoWrapState(int sourceLength, double averageCharacterWidth)
    {
        private double _averageCharacterWidth = Math.Max(0.01, averageCharacterWidth);

        public double EstimatedWidth => sourceLength * _averageCharacterWidth;

        public int GetOffsetForX(double x)
            => Math.Clamp((int)Math.Floor(Math.Max(0, x) / _averageCharacterWidth), 0, sourceLength);

        public double GetXForOffset(int offset)
            => Math.Clamp(offset, 0, sourceLength) * _averageCharacterWidth;

        public int GetLengthForWidth(double width)
            => Math.Max(1, (int)Math.Ceiling(Math.Max(1, width) / _averageCharacterWidth));

        public void Refine(int materializedLength, double measuredWidth)
        {
            if (materializedLength <= 0 || measuredWidth <= 0)
            {
                return;
            }
            double observed = measuredWidth / materializedLength;
            _averageCharacterWidth = Math.Max(0.01, _averageCharacterWidth * 0.75 + observed * 0.25);
        }
    }

    private sealed class LineMetricsIndex
    {
        private double[] _heightTree = [];
        private double[] _widthTree = [];
        private int _leafBase;
        private int _count;

        public LineMetricsIndex(LineState[] states) => Reset(states);

        public double TotalHeight => _count == 0 ? 0 : _heightTree[1];
        public double MaxWidth => _count == 0 ? 0 : _widthTree[1];

        public void Reset(LineState[] states)
        {
            _count = states.Length;
            _leafBase = 1;
            while (_leafBase < Math.Max(1, _count)) _leafBase <<= 1;
            _heightTree = new double[_leafBase * 2];
            _widthTree = new double[_leafBase * 2];
            for (int index = 0; index < states.Length; index++)
            {
                _heightTree[_leafBase + index] = states[index].Height;
                _widthTree[_leafBase + index] = states[index].ExtentWidth;
            }
            for (int node = _leafBase - 1; node > 0; node--)
            {
                _heightTree[node] = _heightTree[node * 2] + _heightTree[node * 2 + 1];
                _widthTree[node] = Math.Max(_widthTree[node * 2], _widthTree[node * 2 + 1]);
            }
        }

        public void Update(int index, double height, double width)
        {
            int node = _leafBase + index;
            _heightTree[node] = height;
            _widthTree[node] = width;
            for (node >>= 1; node > 0; node >>= 1)
            {
                _heightTree[node] = _heightTree[node * 2] + _heightTree[node * 2 + 1];
                _widthTree[node] = Math.Max(_widthTree[node * 2], _widthTree[node * 2 + 1]);
            }
        }

        public double GetLineY(int lineNumber)
        {
            int left = _leafBase;
            int right = _leafBase + Math.Clamp(lineNumber, 0, _count);
            double sum = 0;
            while (left < right)
            {
                if ((left & 1) != 0) sum += _heightTree[left++];
                if ((right & 1) != 0) sum += _heightTree[--right];
                left >>= 1;
                right >>= 1;
            }
            return sum;
        }

        public int FindLineByY(double y)
        {
            if (_count == 0) return 0;
            y = Math.Max(0, y);
            if (y >= TotalHeight) return _count - 1;
            int node = 1;
            while (node < _leafBase)
            {
                int left = node * 2;
                if (y < _heightTree[left])
                {
                    node = left;
                }
                else
                {
                    y -= _heightTree[left];
                    node = left + 1;
                }
            }
            return Math.Min(_count - 1, node - _leafBase);
        }
    }
}
