using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>Native text-layout realization of managed text layouts for Direct2D.</summary>
internal sealed class Direct2DTextRenderContext : ITextRenderContext, IDisposable
{
    private const int RealizationCapacity = 128;
    private readonly Direct2DGraphicsContext _context;
    private readonly LegacyTextRenderContext _fastPathRenderer;
    private readonly BoundedCache<RealizationKey, RealizedRun> _cache = new(
        RealizationCapacity,
        static run => run.Dispose());

    public Direct2DTextRenderContext(Direct2DGraphicsContext context)
    {
        _context = context;
        _fastPathRenderer = new LegacyTextRenderContext(context);
    }

    internal int CachedRunCount => _cache.Count;
    internal IEnumerable<TextLayout> CachedLayouts
        => _cache.Values.Select(static run => run.Layout);

    public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        if (layout is not ManagedTextLayout managed)
            throw new ArgumentException("The layout was created by a different text engine.", nameof(layout));

        if (LegacyTextRenderContext.CanDrawFastPath(managed, in options))
        {
            _fastPathRenderer.Draw(managed, origin, in options);
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
                var first = clusters[index];
                if (first.Kind == ManagedTextClusterKind.Inline)
                {
                    first.Inline!.Draw(this, new Point(origin.X + first.X, origin.Y + line.Metrics.Bounds.Y));
                    index++;
                    continue;
                }
                if (first.Kind is ManagedTextClusterKind.Tab or ManagedTextClusterKind.NewLine)
                {
                    index++;
                    continue;
                }

                int end = index + 1;
                while (end < clusters.Count &&
                       clusters[end].Kind == ManagedTextClusterKind.Text &&
                       clusters[end].Style == first.Style &&
                       clusters[end].Start == clusters[end - 1].End)
                {
                    end++;
                }
                var last = clusters[end - 1];
                int textLength = last.End - first.Start;
                double width = last.X + last.Width - first.X;
                var bounds = new Rect(
                    origin.X + first.X,
                    origin.Y + line.Metrics.Bounds.Y,
                    Math.Max(1, width),
                    line.Metrics.Bounds.Height);
                var realized = GetOrCreate(managed, first, textLength, width, line.Metrics.Bounds.Height);
                DrawRealized(realized, bounds.Position, options.Foreground);
                DrawForegroundSpans(managed, origin, bounds, first.Start, textLength, realized, options.PaintSpans.Span);
                index = end;
            }
        }
        DrawDecorations(managed, origin, options.PaintSpans.Span);
    }

    private RealizedRun GetOrCreate(
        ManagedTextLayout layout,
        ManagedTextCluster first,
        int textLength,
        double width,
        double height)
    {
        var key = new RealizationKey(layout, first.Start, textLength, first.Font, Math.Round(width, 6), Math.Round(height, 6));
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var format = new TextFormat
        {
            Font = first.Font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.NoWrap,
            Trimming = TextTrimming.None
        };
        var constraints = new TextLayoutConstraints(new Rect(0, 0, Math.Max(1, width), Math.Max(1, height)));
        var nativeLayout = _context.CreateTextLayout(
            layout.Snapshot.Text.AsSpan(first.Start, textLength),
            format,
            in constraints) ?? throw new InvalidOperationException("DirectWrite failed to realize a managed text run.");
        var created = new RealizedRun(
            layout.Snapshot.Text.Substring(first.Start, textLength),
            format,
            nativeLayout,
            Math.Max(1, width),
            Math.Max(1, height));
        _cache.Add(key, created);
        return created;
    }

    private void DrawRealized(RealizedRun realization, Point origin, Color color)
    {
        realization.Layout.EffectiveBounds = new Rect(
            origin.X,
            origin.Y,
            realization.Width,
            realization.Height);
        _context.DrawTextLayout(realization.Text, realization.Format, realization.Layout, color);
    }

    private void DrawForegroundSpans(
        ManagedTextLayout layout,
        Point origin,
        Rect runBounds,
        int textStart,
        int textLength,
        RealizedRun realization,
        ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Foreground is not Color color ||
                span.Range.End <= textStart || span.Range.Start >= textStart + textLength) continue;
            var ranges = new List<Rect>();
            layout.GetRangeBounds(span.Range.Start, span.Range.Length, ranges);
            foreach (var range in ranges)
            {
                var clip = runBounds.Intersect(new Rect(
                    origin.X + range.X,
                    origin.Y + range.Y,
                    range.Width,
                    range.Height));
                if (clip.IsEmpty) continue;
                _context.Save();
                try
                {
                    _context.IntersectClip(clip);
                    DrawRealized(realization, runBounds.Position, color);
                }
                finally
                {
                    _context.Restore();
                }
            }
        }
    }

    private void DrawBackgrounds(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Background is not Color color) continue;
            DrawRangeRectangles(layout, origin, span.Range, color);
        }
    }

    private void DrawOverlays(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextOverlay> overlays)
    {
        foreach (var overlay in overlays)
            DrawRangeRectangles(layout, origin, overlay.Range, overlay.Color);
    }

    private void DrawRangeRectangles(ManagedTextLayout layout, Point origin, TextRange range, Color color)
    {
        var bounds = new List<Rect>();
        layout.GetRangeBounds(range.Start, range.Length, bounds);
        foreach (var rect in bounds)
            _context.FillRectangle(new Rect(origin.X + rect.X, origin.Y + rect.Y, rect.Width, rect.Height), color);
    }

    private void DrawDecorations(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Decoration == TextDecoration.None) continue;
            var bounds = new List<Rect>();
            layout.GetRangeBounds(span.Range.Start, span.Range.Length, bounds);
            Color color = span.Foreground ?? Color.FromArgb(255, 0, 0, 0);
            foreach (var rect in bounds)
            {
                if (span.Decoration.HasFlag(TextDecoration.Underline))
                    _context.FillRectangle(new Rect(origin.X + rect.X, origin.Y + rect.Bottom - 1, rect.Width, 1), color);
                if (span.Decoration.HasFlag(TextDecoration.Strikethrough))
                    _context.FillRectangle(new Rect(origin.X + rect.X, origin.Y + rect.Y + (rect.Height - 1) * 0.55, rect.Width, 1), color);
            }
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        _fastPathRenderer.Dispose();
    }

    private readonly record struct RealizationKey(
        ManagedTextLayout Layout,
        int TextStart,
        int TextLength,
        IFont Font,
        double Width,
        double Height);

    private sealed class RealizedRun(
        string text,
        TextFormat format,
        TextLayout layout,
        double width,
        double height) : IDisposable
    {
        public string Text { get; } = text;
        public TextFormat Format { get; } = format;
        public TextLayout Layout { get; } = layout;
        public double Width { get; } = width;
        public double Height { get; } = height;

        public void Dispose() => Layout.ReleaseBackendHandle();
    }
}
