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
    private readonly IGraphicsContext _context;
    private readonly ConditionalWeakTable<ManagedTextLayout, Dictionary<LegacyRunKey, Rendering.TextLayout>> _layouts = new();
    private int _cachedLayoutCount;
    private readonly HashSet<Rendering.TextLayout> _ownedLayouts = [];

    public LegacyTextRenderContext(IGraphicsContext context) => _context = context;

    internal int CachedLayoutCount => _cachedLayoutCount;
    internal IReadOnlyCollection<Rendering.TextLayout> CachedLayouts => _ownedLayouts;

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
                    textStart,
                    textLength,
                    cluster.Font,
                    Math.Round(runWidth, 6),
                    Math.Round(line.Metrics.Bounds.Height, 6));
                var realized = _layouts.GetValue(managed, static _ => []);
                if (!realized.TryGetValue(runKey, out var legacyLayout))
                {
                    var layoutBounds = new Rect(0, 0, Math.Max(1, runWidth), line.Metrics.Bounds.Height);
                    var constraints = new TextLayoutConstraints(layoutBounds);
                    legacyLayout = _context.CreateTextLayout(
                        managed.Snapshot.Text.AsSpan(textStart, textLength),
                        format,
                        in constraints);
                    if (legacyLayout is not null)
                    {
                        realized.Add(runKey, legacyLayout);
                        _ownedLayouts.Add(legacyLayout);
                        _cachedLayoutCount++;
                    }
                }
                if (legacyLayout is not null)
                {
                    legacyLayout.EffectiveBounds = bounds;
                    DrawLegacyRun(managed, textStart, textLength, format, legacyLayout, options.Foreground, options.Owner);
                    DrawForegroundSpans(
                        managed,
                        origin,
                        bounds,
                        textStart,
                        textLength,
                        format,
                        legacyLayout,
                        options.PaintSpans.Span,
                        options.Owner);
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
        var realized = _layouts.GetValue(managed, static _ => []);
        Rect? clip = _context.GetClipBoundsLocal();
        if (clip is Rect visibleClip)
        {
            DrawFastPathVisibleRange(managed, line, origin, visibleClip, color, owner, font, format, realized);
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
                segment.Start,
                segment.Length,
                font,
                Math.Round(bounds.Width, 6),
                Math.Round(bounds.Height, 6));
            if (!realized.TryGetValue(runKey, out var legacyLayout))
            {
                var constraints = new TextLayoutConstraints(new Rect(0, 0, bounds.Width, bounds.Height));
                legacyLayout = _context.CreateTextLayout(
                    managed.Snapshot.Text.AsSpan(segment.Start, segment.Length),
                    format,
                    in constraints);
                if (legacyLayout is not null)
                {
                    realized.Add(runKey, legacyLayout);
                    _ownedLayouts.Add(legacyLayout);
                    _cachedLayoutCount++;
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
        TextFormat format,
        Dictionary<LegacyRunKey, Rendering.TextLayout> realized)
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
            textStart,
            textEnd - textStart,
            font,
            Math.Round(bounds.Width, 6),
            Math.Round(bounds.Height, 6));
        if (!realized.TryGetValue(runKey, out var legacyLayout))
        {
            var constraints = new TextLayoutConstraints(new Rect(0, 0, bounds.Width, bounds.Height));
            legacyLayout = _context.CreateTextLayout(
                managed.Snapshot.Text.AsSpan(textStart, textEnd - textStart),
                format,
                in constraints);
            if (legacyLayout is not null)
            {
                realized.Add(runKey, legacyLayout);
                _ownedLayouts.Add(legacyLayout);
                _cachedLayoutCount++;
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

    private void DrawForegroundSpans(
        ManagedTextLayout managed,
        Point origin,
        Rect runBounds,
        int textStart,
        int textLength,
        TextFormat format,
        Rendering.TextLayout legacyLayout,
        ReadOnlySpan<TextPaintSpan> spans,
        object? owner)
    {
        foreach (var span in spans)
        {
            if (span.Foreground is not Color color ||
                span.Range.End <= textStart ||
                span.Range.Start >= textStart + textLength)
            {
                continue;
            }

            var rangeBounds = new List<Rect>();
            managed.GetRangeBounds(span.Range.Start, span.Range.Length, rangeBounds);
            foreach (var range in rangeBounds)
            {
                var translated = new Rect(origin.X + range.X, origin.Y + range.Y, range.Width, range.Height);
                var clip = runBounds.Intersect(translated);
                if (clip.IsEmpty)
                {
                    continue;
                }

                _context.Save();
                try
                {
                    _context.IntersectClip(clip);
                    DrawLegacyRun(managed, textStart, textLength, format, legacyLayout, color, owner);
                }
                finally
                {
                    _context.Restore();
                }
            }
        }
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
        int TextStart,
        int TextLength,
        IFont Font,
        double Width,
        double Height);

    public void Dispose()
    {
        foreach (var layout in _ownedLayouts)
        {
            layout.ReleaseBackendHandle();
        }
        _ownedLayouts.Clear();
        _cachedLayoutCount = 0;
    }
}
