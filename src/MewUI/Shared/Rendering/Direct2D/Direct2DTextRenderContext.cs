using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>Native text-layout realization of managed text layouts for Direct2D.</summary>
internal sealed class Direct2DTextRenderContext : ITextRenderContext, IDisposable
{
    private const int RealizationCapacity = 128;
    private const string Ellipsis = "...";
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
        var managed = Validate(layout);
        if (LegacyTextRenderContext.CanDrawFastPath(managed, in options))
        {
            _fastPathRenderer.Draw(managed, origin, in options);
            return;
        }
        DrawBackgroundCore(managed, origin, in options);
        DrawForegroundCore(managed, origin, in options);
    }

    public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        var managed = Validate(layout);
        if (!LegacyTextRenderContext.CanDrawFastPath(managed, in options))
        {
            DrawBackgroundCore(managed, origin, in options);
        }
    }

    public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        var managed = Validate(layout);
        if (LegacyTextRenderContext.CanDrawFastPath(managed, in options))
        {
            _fastPathRenderer.Draw(managed, origin, in options);
            return;
        }
        DrawForegroundCore(managed, origin, in options);
    }

    private static ManagedTextLayout Validate(ITextLayout layout)
    {
        if (layout is not ManagedTextLayout managed)
            throw new ArgumentException("The layout was created by a different text engine.", nameof(layout));
        return managed;
    }

    private void DrawBackgroundCore(ManagedTextLayout managed, Point origin, in TextDrawOptions options)
    {
        DrawBackgrounds(managed, origin, options.PaintSpans.Span);
        DrawOverlays(managed, origin, options.Overlays.Span);
    }

    private void DrawForegroundCore(ManagedTextLayout managed, Point origin, in TextDrawOptions options)
    {
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
                DrawRunColorSegments(clusters, index, end, origin, bounds, line.Metrics.Baseline, realized, in options);
                index = end;
            }

            if (line.IsTrimmed)
            {
                DrawEllipsis(managed, line, clusters, origin, options.Foreground);
            }
        }
        DrawDecorations(managed, origin, options.PaintSpans.Span);
    }

    /// <summary>Draws the trimming ellipsis after the last surviving cluster of a trimmed line.</summary>
    private void DrawEllipsis(
        ManagedTextLayout managed,
        ManagedTextLine line,
        List<ManagedTextCluster> clusters,
        Point origin,
        Color color)
    {
        var lineBounds = line.Metrics.Bounds;
        var font = clusters.Count > 0 ? clusters[^1].Font : managed.GetDefaultFont();
        double x = clusters.Count > 0 ? clusters[^1].X + clusters[^1].Width : lineBounds.X;
        double width = Math.Max(1, lineBounds.Right - x);
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.NoWrap,
            Trimming = TextTrimming.None
        };
        var constraints = new TextLayoutConstraints(new Rect(0, 0, width, lineBounds.Height));
        var layout = _context.CreateTextLayout(Ellipsis, format, in constraints);
        if (layout is null) return;

        layout.EffectiveBounds = new Rect(origin.X + x, origin.Y + lineBounds.Y, width, lineBounds.Height);
        _context.DrawTextLayout(Ellipsis, format, layout, color);
        layout.ReleaseBackendHandle();
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

    /// <summary>
    /// Draws one style run partitioned into effective-foreground segments so every pixel is
    /// painted exactly once. ClearType blends subpixel coverage against the destination, so
    /// overdrawing span colors onto the base pass would fringe; single-pass clipping keeps
    /// each glyph blended against the clean background.
    /// </summary>
    private void DrawRunColorSegments(
        List<ManagedTextCluster> clusters,
        int firstCluster,
        int endCluster,
        Point origin,
        Rect runBounds,
        double baseline,
        RealizedRun realized,
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
                DrawRealized(realized, runBounds.Position, segmentColor);
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
                        DrawRealized(realized, runBounds.Position, segmentColor);
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

    /// <summary>
    /// Draws style-run underline/strikethrough as renderer geometry so decorations match the
    /// engine-drawn path used by the other render contexts.
    /// </summary>
    private void DrawRunDecoration(TextRunStyle style, double left, double right, in Rect runBounds, double baseline, Color color)
    {
        if (style.Decoration == TextDecoration.None) return;

        double clampedLeft = Math.Max(left, runBounds.X);
        double width = Math.Min(right, runBounds.Right) - clampedLeft;
        if (width <= 0) return;

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
