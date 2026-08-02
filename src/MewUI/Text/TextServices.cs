using System.Runtime.CompilerServices;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal static class TextServices
{
    private static readonly ConditionalWeakTable<IGraphicsFactory, ManagedTextEngine> Engines = new();
    private static readonly ConditionalWeakTable<IGraphicsContext, ITextRenderContext> RenderContexts = new();

    public static ITextEngine GetEngine(IGraphicsFactory factory)
        => Engines.GetValue(factory, static value => new ManagedTextEngine(value));

    public static ITextRenderContext GetRenderContext(IGraphicsContext context)
        => RenderContexts.GetValue(context, static value => new LegacyTextRenderContext(value));

    public static void ReleaseRenderContext(IGraphicsContext context)
    {
        if (RenderContexts.TryGetValue(context, out var renderContext))
        {
            RenderContexts.Remove(context);
            (renderContext as IDisposable)?.Dispose();
        }
    }

    public static void ReleaseEngine(IGraphicsFactory factory)
    {
        if (Engines.TryGetValue(factory, out var engine))
        {
            Engines.Remove(factory);
            engine.Dispose();
        }
    }
}

internal sealed class LegacyTextRenderContext : ITextRenderContext, IDisposable
{
    private const int RealizationCapacity = 128;
    private readonly IGraphicsContext _context;
    private readonly BoundedCache<LegacyRunKey, Rendering.TextLayout> _layouts = new(
        RealizationCapacity,
        static layout => layout.ReleaseBackendHandle());

    public LegacyTextRenderContext(IGraphicsContext context) => _context = context;

    internal int CachedLayoutCount => _layouts.Count;
    internal IReadOnlyCollection<Rendering.TextLayout> CachedLayouts => _layouts.Values;

    public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout is not ManagedTextLayout managed)
        {
            throw new ArgumentException("The layout was created by a different text engine.", nameof(layout));
        }

        if (CanDrawFastPath(managed, in options))
        {
            DrawFastPath(managed, origin, options.Foreground, options.Owner);
            return;
        }

        DrawBackgrounds(managed, origin, options.PaintSpans.Span);
        DrawOverlays(managed, origin, options.Overlays.Span);

        foreach (var line in managed.ManagedLines)
        {
            var clusters = managed.EnsureClusters(line);
            int index = 0;
            while (index < clusters.Count)
            {
                var cluster = clusters[index];
                if (cluster.Kind == ManagedTextClusterKind.Inline)
                {
                    cluster.Inline!.Draw(this, new Point(origin.X + cluster.X, origin.Y + line.Metrics.Bounds.Y));
                    index++;
                    continue;
                }
                if (cluster.Kind is ManagedTextClusterKind.Tab or ManagedTextClusterKind.NewLine)
                {
                    index++;
                    continue;
                }

                int runEnd = index + 1;
                while (runEnd < clusters.Count)
                {
                    var next = clusters[runEnd];
                    if (next.Kind != ManagedTextClusterKind.Text ||
                        next.Style != cluster.Style ||
                        next.Start != clusters[runEnd - 1].End)
                    {
                        break;
                    }
                    runEnd++;
                }

                var last = clusters[runEnd - 1];
                int textStart = cluster.Start;
                int textLength = last.End - textStart;
                double runWidth = last.X + last.Width - cluster.X;
                var bounds = new Rect(
                    origin.X + cluster.X,
                    origin.Y + line.Metrics.Bounds.Y,
                    Math.Max(1, runWidth),
                    line.Metrics.Bounds.Height);
                var format = new TextFormat
                {
                    Font = cluster.Font,
                    HorizontalAlignment = TextAlignment.Left,
                    VerticalAlignment = TextAlignment.Top,
                    Wrapping = TextWrapping.NoWrap,
                    Trimming = TextTrimming.None
                };
                var runKey = new LegacyRunKey(
                    managed,
                    textStart,
                    textLength,
                    cluster.Font,
                    Math.Round(runWidth, 6),
                    Math.Round(line.Metrics.Bounds.Height, 6));
                if (!_layouts.TryGetValue(runKey, out var legacyLayout))
                {
                    var layoutBounds = new Rect(0, 0, Math.Max(1, runWidth), line.Metrics.Bounds.Height);
                    var constraints = new TextLayoutConstraints(layoutBounds);
                    legacyLayout = _context.CreateTextLayout(
                        managed.Snapshot.Text.AsSpan(textStart, textLength),
                        format,
                        in constraints);
                    if (legacyLayout is not null)
                    {
                        _layouts.Add(runKey, legacyLayout);
                    }
                }
                if (legacyLayout is not null)
                {
                    legacyLayout.EffectiveBounds = bounds;
                    DrawRunColorSegments(
                        managed, clusters, index, runEnd, origin, bounds,
                        line.Metrics.Baseline, textStart, textLength, format, legacyLayout, in options);
                }
                index = runEnd;
            }
        }

        DrawDecorations(managed, origin, options.PaintSpans.Span);
    }

    internal static bool CanDrawFastPath(ManagedTextLayout layout, in TextDrawOptions options)
        => layout.IsFastPath &&
           !layout.HasMaterializedClusters &&
           options.PaintSpans.IsEmpty &&
           options.Overlays.IsEmpty;

    private void DrawFastPath(ManagedTextLayout managed, Point origin, Color color, object? owner)
    {
        var line = managed.ManagedLines[0];
        var font = managed.GetDefaultFont();
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.NoWrap,
            Trimming = TextTrimming.None
        };
        Rect? clip = _context.GetClipBoundsLocal();
        if (clip is Rect visibleClip)
        {
            DrawFastPathVisibleRange(managed, line, origin, visibleClip, color, owner, font, format);
            return;
        }

        foreach (var segment in line.FastSegments ?? [])
        {
            var bounds = new Rect(
                origin.X + segment.X,
                origin.Y + line.Metrics.Bounds.Y,
                Math.Max(1, segment.Width),
                line.Metrics.Bounds.Height);
            if (clip is Rect clipBounds && bounds.Intersect(clipBounds).IsEmpty)
            {
                continue;
            }

            var runKey = new LegacyRunKey(
                managed,
                segment.Start,
                segment.Length,
                font,
                Math.Round(bounds.Width, 6),
                Math.Round(bounds.Height, 6));
            if (!_layouts.TryGetValue(runKey, out var legacyLayout))
            {
                var constraints = new TextLayoutConstraints(new Rect(0, 0, bounds.Width, bounds.Height));
                legacyLayout = _context.CreateTextLayout(
                    managed.Snapshot.Text.AsSpan(segment.Start, segment.Length),
                    format,
                    in constraints);
                if (legacyLayout is not null)
                {
                    _layouts.Add(runKey, legacyLayout);
                }
            }
            if (legacyLayout is null)
            {
                continue;
            }

            legacyLayout.EffectiveBounds = bounds;
            DrawLegacyRun(
                managed,
                segment.Start,
                segment.Length,
                format,
                legacyLayout,
                color,
                owner);
        }
    }

    private void DrawFastPathVisibleRange(
        ManagedTextLayout managed,
        ManagedTextLine line,
        Point origin,
        Rect clip,
        Color color,
        object? owner,
        IFont font,
        TextFormat format)
    {
        const double Overscan = 32;
        double hitY = line.Metrics.Bounds.Y + line.Metrics.Bounds.Height * 0.5;
        CharacterHit startHit = managed.HitTestPoint(new Point(clip.Left - origin.X - Overscan, hitY));
        CharacterHit endHit = managed.HitTestPoint(new Point(clip.Right - origin.X + Overscan, hitY));
        int textStart = Math.Clamp(startHit.FirstCharacterIndex, 0, managed.Snapshot.Text.Length);
        int textEnd = Math.Clamp(endHit.InsertionIndex, textStart, managed.Snapshot.Text.Length);
        if (textEnd <= textStart)
        {
            return;
        }

        Rect startCaret = managed.GetCaretBounds(new CharacterHit(textStart, 0));
        Rect endCaret = managed.GetCaretBounds(new CharacterHit(textEnd, 0));
        var bounds = new Rect(
            origin.X + startCaret.X,
            origin.Y + line.Metrics.Bounds.Y,
            Math.Max(1, endCaret.X - startCaret.X),
            line.Metrics.Bounds.Height);
        var runKey = new LegacyRunKey(
            managed,
            textStart,
            textEnd - textStart,
            font,
            Math.Round(bounds.Width, 6),
            Math.Round(bounds.Height, 6));
        if (!_layouts.TryGetValue(runKey, out var legacyLayout))
        {
            var constraints = new TextLayoutConstraints(new Rect(0, 0, bounds.Width, bounds.Height));
            legacyLayout = _context.CreateTextLayout(
                managed.Snapshot.Text.AsSpan(textStart, textEnd - textStart),
                format,
                in constraints);
            if (legacyLayout is not null)
            {
                _layouts.Add(runKey, legacyLayout);
            }
        }
        if (legacyLayout is null)
        {
            return;
        }

        legacyLayout.EffectiveBounds = bounds;
        DrawLegacyRun(
            managed,
            textStart,
            textEnd - textStart,
            format,
            legacyLayout,
            color,
            owner);
    }

    private void DrawBackgrounds(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Background is not Color color)
            {
                continue;
            }
            var bounds = new List<Rect>();
            layout.GetRangeBounds(span.Range.Start, span.Range.Length, bounds);
            foreach (var rect in bounds)
            {
                _context.FillRectangle(new Rect(origin.X + rect.X, origin.Y + rect.Y, rect.Width, rect.Height), color);
            }
        }
    }

    private void DrawOverlays(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextOverlay> overlays)
    {
        foreach (var overlay in overlays)
        {
            var bounds = new List<Rect>();
            layout.GetRangeBounds(overlay.Range.Start, overlay.Range.Length, bounds);
            foreach (var rect in bounds)
            {
                _context.FillRectangle(new Rect(origin.X + rect.X, origin.Y + rect.Y, rect.Width, rect.Height), overlay.Color);
            }
        }
    }

    private void DrawDecorations(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Decoration == TextDecoration.None)
            {
                continue;
            }
            var bounds = new List<Rect>();
            layout.GetRangeBounds(span.Range.Start, span.Range.Length, bounds);
            Color color = span.Foreground ?? Color.FromArgb(255, 0, 0, 0);
            foreach (var rect in bounds)
            {
                if (span.Decoration.HasFlag(TextDecoration.Underline))
                {
                    _context.FillRectangle(new Rect(
                        origin.X + rect.X,
                        origin.Y + Math.Max(rect.Y, rect.Bottom - 1),
                        rect.Width,
                        1), color);
                }
                if (span.Decoration.HasFlag(TextDecoration.Strikethrough))
                {
                    _context.FillRectangle(new Rect(
                        origin.X + rect.X,
                        origin.Y + rect.Y + Math.Max(0, (rect.Height - 1) * 0.55),
                        rect.Width,
                        1), color);
                }
            }
        }
    }

    /// <summary>
    /// Draws one style run partitioned into effective-foreground segments so every pixel is
    /// painted exactly once. The geometry realization stays whole-run (paint spans never split
    /// or recreate it); only the draw is clipped per color segment, avoiding the old overdraw
    /// pass that blended the base color into antialiased glyph edges.
    /// </summary>
    private void DrawRunColorSegments(
        ManagedTextLayout managed,
        List<ManagedTextCluster> clusters,
        int firstCluster,
        int endCluster,
        Point origin,
        Rect runBounds,
        double baseline,
        int textStart,
        int textLength,
        TextFormat format,
        Rendering.TextLayout legacyLayout,
        in TextDrawOptions options)
    {
        var spans = options.PaintSpans.Span;
        int segmentStart = firstCluster;
        var segmentColor = GetSpanForeground(spans, clusters[firstCluster].Start) ?? options.Foreground;

        for (int clusterIndex = firstCluster + 1; clusterIndex <= endCluster; clusterIndex++)
        {
            Color nextColor = default;
            if (clusterIndex < endCluster)
            {
                nextColor = GetSpanForeground(spans, clusters[clusterIndex].Start) ?? options.Foreground;
                if (nextColor == segmentColor)
                {
                    continue;
                }
            }

            var startCluster = clusters[segmentStart];
            var lastCluster = clusters[clusterIndex - 1];
            double left = origin.X + startCluster.X;
            double right = origin.X + lastCluster.X + lastCluster.Width;
            if (segmentStart == firstCluster && clusterIndex == endCluster)
            {
                DrawLegacyRun(managed, textStart, textLength, format, legacyLayout, segmentColor, options.Owner);
            }
            else
            {
                // Interior color boundaries floor to whole device pixels so adjacent clips agree
                // on pixel ownership; backend clip rounding otherwise shifts the boundary column
                // into the neighbor color depending on the fractional scroll offset.
                double dpiScale = _context.DpiScale;
                double clipLeft = segmentStart == firstCluster ? runBounds.X : Math.Floor(left * dpiScale) / dpiScale;
                double clipRight = clusterIndex == endCluster ? runBounds.Right : Math.Floor(right * dpiScale) / dpiScale;
                var clip = new Rect(clipLeft, runBounds.Y, Math.Max(0, clipRight - clipLeft), runBounds.Height).Intersect(runBounds);
                if (!clip.IsEmpty)
                {
                    _context.Save();
                    try
                    {
                        _context.IntersectClip(clip);
                        DrawLegacyRun(managed, textStart, textLength, format, legacyLayout, segmentColor, options.Owner);
                    }
                    finally
                    {
                        _context.Restore();
                    }
                }
            }
            DrawRunDecoration(startCluster.Style, left, right, runBounds, baseline, segmentColor);

            segmentStart = clusterIndex;
            segmentColor = nextColor;
        }
    }

    /// <summary>
    /// Draws style-run underline/strikethrough as renderer geometry so every backend matches;
    /// font-level decoration support varies by backend and is not relied on.
    /// </summary>
    private void DrawRunDecoration(TextRunStyle style, double left, double right, in Rect runBounds, double baseline, Color color)
    {
        if (style.Decoration == TextDecoration.None)
        {
            return;
        }

        double clampedLeft = Math.Max(left, runBounds.X);
        double width = Math.Min(right, runBounds.Right) - clampedLeft;
        if (width <= 0)
        {
            return;
        }

        // Pixel-snap the line position and thickness so antialiasing does not smear the stroke.
        double dpiScale = _context.DpiScale;
        double thickness = LayoutRounding.SnapThicknessToPixels(1, dpiScale, 1);
        if (style.Decoration.HasFlag(TextDecoration.Underline))
        {
            double y = LayoutRounding.RoundToPixel(Math.Min(runBounds.Y + baseline + 1, runBounds.Bottom - thickness), dpiScale);
            _context.FillRectangle(new Rect(clampedLeft, y, width, thickness), color);
        }
        if (style.Decoration.HasFlag(TextDecoration.Strikethrough))
        {
            // FontSize is in points; 4/3 converts to DIPs, strike sits ~30% of the em above baseline.
            double y = LayoutRounding.RoundToPixel(runBounds.Y + baseline - style.FontSize * (4.0 / 3.0) * 0.3, dpiScale);
            _context.FillRectangle(new Rect(clampedLeft, y, width, thickness), color);
        }
    }

    /// <summary>
    /// Resolves the effective foreground for a character index: the last covering paint span
    /// wins, matching painter-order span semantics.
    /// </summary>
    private static Color? GetSpanForeground(ReadOnlySpan<TextPaintSpan> spans, int index)
    {
        Color? result = null;
        foreach (var span in spans)
        {
            if (span.Foreground is Color color && index >= span.Range.Start && index < span.Range.End)
            {
                result = color;
            }
        }
        return result;
    }

    private void DrawLegacyRun(
        ManagedTextLayout managed,
        int textStart,
        int textLength,
        TextFormat format,
        Rendering.TextLayout legacyLayout,
        Color color,
        object? owner)
    {
        _context.DrawTextLayout(
            managed.Snapshot.Text.AsSpan(textStart, textLength),
            format,
            legacyLayout,
            color,
            owner);
    }

    private readonly record struct LegacyRunKey(
        ManagedTextLayout Layout,
        int TextStart,
        int TextLength,
        IFont Font,
        double Width,
        double Height);

    public void Dispose() => _layouts.Dispose();
}

internal sealed class BoundedCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly int _capacity;
    private readonly Action<TValue> _dispose;
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly LinkedList<TKey> _order = [];

    public BoundedCache(int capacity, Action<TValue> dispose)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    public int Count => _entries.Count;
    public IReadOnlyCollection<TValue> Values => _entries.Values.Select(static entry => entry.Value).ToArray();

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            _order.Remove(entry.Node);
            _order.AddLast(entry.Node);
            value = entry.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        if (_entries.Remove(key, out var replaced))
        {
            _order.Remove(replaced.Node);
            _dispose(replaced.Value);
        }
        var node = _order.AddLast(key);
        _entries.Add(key, new Entry(value, node));
        while (_entries.Count > _capacity && _order.First is { } oldest)
        {
            _order.RemoveFirst();
            if (_entries.Remove(oldest.Value, out var evicted))
            {
                _dispose(evicted.Value);
            }
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            _dispose(entry.Value);
        }
        _entries.Clear();
        _order.Clear();
    }

    private sealed record Entry(TValue Value, LinkedListNode<TKey> Node);
}
