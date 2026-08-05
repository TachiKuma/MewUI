using System.Collections;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>List that reports mutations so the view can repaint.</summary>
internal sealed class ExtensionList<T>(Action onChanged) : IList<T>
{
    private readonly List<T> _items = [];

    public T this[int index]
    {
        get => _items[index];
        set { _items[index] = value; onChanged(); }
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(T item) { _items.Add(item); onChanged(); }
    public void Insert(int index, T item) { _items.Insert(index, item); onChanged(); }
    public bool Remove(T item) { bool removed = _items.Remove(item); if (removed) onChanged(); return removed; }
    public void RemoveAt(int index) { _items.RemoveAt(index); onChanged(); }
    public void Clear() { _items.Clear(); onChanged(); }
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public int IndexOf(T item) => _items.IndexOf(item);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Runs the registered <see cref="IVisualLineTransformer"/>s and translates their element overrides
/// into engine paint spans and geometry runs. Registered as both a classifier and a transformer
/// because colors and fonts travel through different pipeline stages; the per-line result is
/// computed once and shared between the two calls.
/// </summary>
internal sealed class LineTransformerAdapter(TextEditor editor) : ITextClassifier, ITextLineTransformer
{
    private readonly List<VisualLineElement> _elements = [];
    private readonly TransformContext _context = new(editor);
    private long _cachedVersion = -1;
    private int _cachedOffset = -1;
    private int _cachedLength = -1;

    public IList<IVisualLineTransformer> Transformers { get; } =
        new ExtensionList<IVisualLineTransformer>(editor.InvalidateTextView);

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        EnsureComputed(context.LogicalLine);
        foreach (var element in _elements)
        {
            var properties = element.TextRunProperties;
            var foreground = element.Foreground ?? properties.ForegroundBrush;
            var background = element.BackgroundBrush ?? properties.BackgroundBrush;
            if (!foreground.HasValue && !background.HasValue && properties.TextDecorations == TextDecoration.None)
            {
                continue;
            }
            output.Add(new TextPaintSpan(
                new TextRange(element.RelativeTextOffset, element.DocumentLength),
                foreground,
                background,
                properties.TextDecorations));
        }
    }

    public void Transform(
        in TextLineTransformContext context,
        IList<GeometryStyleRun> geometryRuns,
        IList<InlineRun> inlines)
    {
        EnsureComputed(context.LogicalLine);
        foreach (var element in _elements)
        {
            var properties = element.TextRunProperties;
            if (!properties.HasFont)
            {
                continue;
            }
            var style = context.DefaultStyle with
            {
                FontFamily = properties.FontFamily ?? context.DefaultStyle.FontFamily,
                FontSize = properties.FontRenderingEmSize ?? context.DefaultStyle.FontSize,
                Weight = properties.FontWeight ?? context.DefaultStyle.Weight,
                Italic = properties.Italic ?? context.DefaultStyle.Italic
            };
            geometryRuns.Add(new GeometryStyleRun(element.RelativeTextOffset, element.DocumentLength, style));
        }
    }

    private void EnsureComputed(in LogicalTextLine logical)
    {
        long version = editor.Document.Version;
        if (_cachedVersion == version && _cachedOffset == logical.Offset && _cachedLength == logical.Length)
        {
            return;
        }
        _cachedVersion = version;
        _cachedOffset = logical.Offset;
        _cachedLength = logical.Length;
        _elements.Clear();
        if (Transformers.Count == 0)
        {
            return;
        }

        _context.CurrentDocumentLine = editor.Document.GetLineByOffset(logical.Offset);
        foreach (var transformer in Transformers)
        {
            transformer.Transform(_context, _elements);
        }
    }

    private sealed class TransformContext(TextEditor editor) : ITextRunConstructionContext
    {
        public TextDocument Document => editor.Document;
        public TextView TextView => editor.TextArea.TextView;
        public DocumentLine CurrentDocumentLine { get; set; } = null!;
        public TextRunStyle DefaultStyle => new(editor.FontFamily, editor.FontSize, editor.FontWeight);
    }
}

/// <summary>
/// Runs the registered <see cref="VisualLineElementGenerator"/>s over a line, following AvalonEdit's
/// scan protocol. One cached scan per line serves three consumers: the projection stage replaces the
/// document text of elements whose visual and document lengths differ, the generation stage turns
/// every element into an engine inline run at its projected position, and input routing looks up
/// the element under a document offset.
/// </summary>
internal sealed class ElementGeneratorAdapter(TextEditor editor)
    : ITextElementGenerator, ITextProjection, ITextClassifier
{
    private readonly GenerationContext _context = new(editor);
    private readonly Dictionary<int, CachedScan> _scans = [];
    private long _scanVersion = -1;

    public IList<VisualLineElementGenerator> Generators { get; } =
        new ExtensionList<VisualLineElementGenerator>(editor.InvalidateTextView);

    public ProjectedText Project(in TextProjectionContext context)
    {
        var identity = new ProjectedText(context.SourceText, IdentityTextOffsetMap.Instance);
        if (Generators.Count == 0)
        {
            return identity;
        }

        var scan = EnsureScanned(context.LogicalLine);
        bool changesLength = false;
        foreach (var element in scan.Elements)
        {
            if (element.VisualLength != element.DocumentLength)
            {
                changesLength = true;
                break;
            }
        }
        if (!changesLength)
        {
            return identity;
        }

        var source = context.SourceText.Span;
        var builder = new System.Text.StringBuilder(source.Length);
        var segments = new List<ReplacementOffsetMap.Segment>();
        int consumed = 0;
        foreach (var element in scan.Elements)
        {
            if (element.VisualLength == element.DocumentLength)
            {
                continue;
            }
            int start = element.RelativeTextOffset;
            if (start < consumed || start + element.DocumentLength > source.Length)
            {
                continue;
            }
            builder.Append(source[consumed..start]);
            string visual = element.GetVisualText();
            segments.Add(new ReplacementOffsetMap.Segment(
                start, element.DocumentLength, builder.Length, visual.Length));
            builder.Append(visual);
            consumed = start + element.DocumentLength;
        }
        builder.Append(source[consumed..]);
        return new ProjectedText(builder.ToString().AsMemory(), new ReplacementOffsetMap([.. segments]));
    }

    public void Generate(in TextElementContext context, IList<InlineRun> output)
    {
        if (Generators.Count == 0)
        {
            return;
        }

        var scan = EnsureScanned(context.LogicalLine);
        foreach (var element in scan.Elements)
        {
            if (!element.ReplacesText)
            {
                continue;
            }
            int start = context.OffsetMap.MapFromSource(element.RelativeTextOffset);
            int end = context.OffsetMap.MapFromSource(element.RelativeTextOffset + element.DocumentLength);
            if (end > start)
            {
                output.Add(new InlineRun(start, end - start, new ElementInline(editor, element)));
            }
        }
    }

    /// <summary>Paints the elements that only decorate their range.</summary>
    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        if (Generators.Count == 0)
        {
            return;
        }

        // Not inline runs: one run is one cluster, which would remove every caret position inside.
        var scan = EnsureScanned(context.LogicalLine);
        foreach (var element in scan.Elements)
        {
            if (element.ReplacesText)
            {
                continue;
            }
            element.PrepareForPaint(editor.TextArea.TextView);
            var properties = element.TextRunProperties;
            var foreground = element.Foreground ?? properties.ForegroundBrush;
            var background = element.BackgroundBrush ?? properties.BackgroundBrush;
            if (!foreground.HasValue && !background.HasValue && properties.TextDecorations == TextDecoration.None)
            {
                continue;
            }
            int start = context.OffsetMap.MapFromSource(element.RelativeTextOffset);
            int end = context.OffsetMap.MapFromSource(element.RelativeTextOffset + element.DocumentLength);
            if (end > start)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(start, end - start), foreground, background, properties.TextDecorations));
            }
        }
    }

    /// <summary>Elements of an already scanned line, keyed by its laid-out start offset.</summary>
    public IReadOnlyList<VisualLineElement> GetScannedElements(int lineOffset)
        => editor.Document.Version == _scanVersion && _scans.TryGetValue(lineOffset, out var scan)
            ? scan.Elements
            : Array.Empty<VisualLineElement>();

    /// <summary>Element covering the document offset on an already scanned line, if any.</summary>
    public VisualLineElement? FindElementAt(int documentOffset)
    {
        if (editor.Document.Version != _scanVersion)
        {
            return null;
        }
        foreach ((int lineStart, var scan) in _scans)
        {
            if (documentOffset < lineStart || documentOffset >= lineStart + scan.Length)
            {
                continue;
            }
            int relative = documentOffset - lineStart;
            foreach (var element in scan.Elements)
            {
                if (relative >= element.RelativeTextOffset &&
                    relative < element.RelativeTextOffset + Math.Max(1, element.DocumentLength))
                {
                    return element;
                }
            }
            return null;
        }
        return null;
    }

    private CachedScan EnsureScanned(in LogicalTextLine logical)
    {
        long version = editor.Document.Version;
        if (version != _scanVersion)
        {
            _scans.Clear();
            _scanVersion = version;
        }
        if (_scans.TryGetValue(logical.Offset, out var cached) && cached.Length == logical.Length)
        {
            return cached;
        }

        var scan = new CachedScan(logical.Length, RunScan(logical));
        _scans[logical.Offset] = scan;
        return scan;
    }

    private List<VisualLineElement> RunScan(in LogicalTextLine logical)
    {
        var elements = new List<VisualLineElement>();
        int lineStart = logical.Offset;
        int lineEnd = lineStart + logical.Length;
        _context.CurrentDocumentLine = editor.Document.GetLineByOffset(lineStart);
        foreach (var generator in Generators)
        {
            generator.StartGeneration(_context);
        }
        try
        {
            int offset = lineStart;
            while (offset < lineEnd)
            {
                int bestOffset = int.MaxValue;
                VisualLineElementGenerator? winner = null;
                foreach (var generator in Generators)
                {
                    int interested = generator.GetFirstInterestedOffset(offset);
                    if (interested >= offset && interested < bestOffset && interested < lineEnd)
                    {
                        bestOffset = interested;
                        winner = generator;
                    }
                }
                if (winner is null)
                {
                    break;
                }

                var element = winner.ConstructElement(bestOffset);
                if (element is null)
                {
                    // Declining the offset must still advance, or the scan would not terminate.
                    offset = bestOffset + 1;
                    continue;
                }
                element.RelativeTextOffset = bestOffset - lineStart;
                elements.Add(element);
                offset = bestOffset + Math.Max(1, element.DocumentLength);
            }
        }
        finally
        {
            foreach (var generator in Generators)
            {
                generator.FinishGeneration();
            }
        }
        return elements;
    }

    private readonly record struct CachedScan(int Length, List<VisualLineElement> Elements);

    // Reads the density on each call rather than storing it on the element, so a DPI change needs
    // no rescan: the scan cache only invalidates on a document change.
    private sealed class ElementInline(TextEditor editor, VisualLineElement element) : IInlineTextObject
    {
        public InlineMetrics Measure() => element.Measure(editor.EditorDpi);

        public void Draw(ITextRenderContext context, Point origin)
            => element.Draw(context, origin, editor.EditorDpi);
    }

    private sealed class GenerationContext(TextEditor editor) : ITextRunConstructionContext
    {
        public TextDocument Document => editor.Document;
        public TextView TextView => editor.TextArea.TextView;
        public DocumentLine CurrentDocumentLine { get; set; } = null!;
        public TextRunStyle DefaultStyle => new(editor.FontFamily, editor.FontSize, editor.FontWeight);
    }
}

/// <summary>
/// Line-relative offset map for ranges whose projected text has a different length than the
/// document text. Offsets inside a replaced range collapse to its start on both axes, which is
/// what places the caret before a folded region rather than inside it.
/// </summary>
internal sealed class ReplacementOffsetMap(ReplacementOffsetMap.Segment[] segments) : ITextOffsetMap
{
    internal readonly record struct Segment(int SourceStart, int SourceLength, int ProjectedStart, int ProjectedLength);

    public int MapFromSource(int sourceOffset)
    {
        int delta = 0;
        foreach (var segment in segments)
        {
            if (sourceOffset < segment.SourceStart)
            {
                break;
            }
            if (sourceOffset < segment.SourceStart + segment.SourceLength)
            {
                return segment.ProjectedStart;
            }
            delta += segment.ProjectedLength - segment.SourceLength;
        }
        return sourceOffset + delta;
    }

    public int MapToSource(int projectedOffset)
    {
        int delta = 0;
        foreach (var segment in segments)
        {
            if (projectedOffset < segment.ProjectedStart)
            {
                break;
            }
            if (projectedOffset < segment.ProjectedStart + segment.ProjectedLength)
            {
                return segment.SourceStart;
            }
            delta += segment.SourceLength - segment.ProjectedLength;
        }
        return projectedOffset + delta;
    }
}

/// <summary>
/// Holds the editor's background renderers and draws them once per frame at each known layer, so a
/// renderer computes geometry for the whole viewport exactly once as it does in AvalonEdit.
/// </summary>
internal sealed class BackgroundRendererRegistry(TextEditor editor)
{
    public IList<IBackgroundRenderer> Renderers { get; } =
        new ExtensionList<IBackgroundRenderer>(editor.InvalidateTextView);

    /// <summary>Inserts one layer under each known anchor; each draws the renderers assigned to it.</summary>
    public void RegisterInto(ITextViewHost host)
    {
        foreach (var layer in Enum.GetValues<KnownLayer>())
        {
            // Below the anchor, because an AvalonEdit background renderer paints under the content
            // of the layer it names.
            host.InsertLayer(
                new LayerBridge(editor, layer, Renderers),
                TextView.ToAnchor(layer),
                TextLayerPosition.Below);
        }
    }

    private sealed class LayerBridge(TextEditor editor, KnownLayer layer, IList<IBackgroundRenderer> renderers)
        : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds)
        {
            foreach (var renderer in renderers)
            {
                if (renderer.Layer == layer)
                {
                    renderer.Draw(editor.TextArea.TextView, context.Graphics);
                }
            }
        }
    }
}
