using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal sealed class ManagedTextEngine : ITextEngine, IDisposable
{
    // GDI DrawText stops reporting reliable extents above its 16-bit-era text limit.
    private const int FastPathSegmentLength = 32 * 1024;
    private readonly IGraphicsFactory _factory;
    private readonly Dictionary<FontKey, IFont> _fonts = [];
    private readonly ManagedTextLayoutCache _cache;
    private bool _disposed;

    public ManagedTextEngine(IGraphicsFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _cache = new ManagedTextLayoutCache(this);
    }

    public ITextLayoutCache ManagedCache => _cache;

    public ITextLayout CreateLayout(TextLayoutRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CreateLayoutCore(TextLayoutRequestSnapshot.Create(request));
    }

    public ITextLayout GetOrCreateLayout(
        TextLayoutRequest request,
        TextLayoutCachePolicy cachePolicy,
        object? owner = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = TextLayoutRequestSnapshot.Create(request);
        return _cache.GetOrCreate(snapshot, cachePolicy, owner);
    }

    internal ManagedTextLayout CreateLayoutCore(TextLayoutRequestSnapshot snapshot)
    {
        bool fastPath = snapshot.Paragraph.Wrapping == TextWrapping.NoWrap &&
                        snapshot.Paragraph.FlowDirection == TextFlowDirection.LeftToRight &&
                        snapshot.Paragraph.LetterSpacing == 0 &&
                        snapshot.Runs.Length == 0 &&
                        snapshot.Inlines.Length == 0 &&
                        snapshot.Text.AsSpan().IndexOfAny('\r', '\n', '\t') < 0;

        using var context = _factory.CreateMeasurementContext(snapshot.Dpi);
        if (fastPath)
        {
            var font = GetFont(snapshot.DefaultStyle, snapshot.Dpi);
            var segments = MeasureFastPathSegments(context, snapshot.Text, font, out var measured);
            double height = ResolveLineHeight(snapshot.Paragraph, font.Ascent + font.Descent, measured.Height);
            double width = measured.Width;
            double x = ResolveLineX(snapshot.Paragraph, width);
            if (x != 0)
            {
                for (int index = 0; index < segments.Count; index++)
                {
                    segments[index] = segments[index] with { X = segments[index].X + x };
                }
            }
            var line = new ManagedTextLine(
                new TextLayoutLineMetrics(0, snapshot.Text.Length, 0, new Rect(x, 0, width, height), font.Ascent),
                clusters: null,
                fastSegments: segments);
            return new ManagedTextLayout(this, snapshot, [line], new Size(width, height), isFastPath: true);
        }

        var clusters = MeasureClusters(context, snapshot, 0, snapshot.Text.Length);
        var lines = AssembleLines(snapshot, clusters);
        double measuredWidth = lines.Count == 0 ? 0 : lines.Max(static line => line.Metrics.Bounds.Width);
        double contentHeight = lines.Count == 0 ? 0 : lines[^1].Metrics.Bounds.Bottom;
        return new ManagedTextLayout(
            this,
            snapshot,
            lines,
            new Size(measuredWidth, contentHeight),
            isFastPath: false);
    }

    internal List<ManagedTextCluster> MeasureClusters(
        TextLayoutRequestSnapshot snapshot,
        int start,
        int length)
    {
        using var context = _factory.CreateMeasurementContext(snapshot.Dpi);
        return MeasureClusters(context, snapshot, start, length);
    }

    internal double MeasureFastPathRange(TextLayoutRequestSnapshot snapshot, int start, int length)
    {
        start = Math.Clamp(start, 0, snapshot.Text.Length);
        length = Math.Clamp(length, 0, snapshot.Text.Length - start);
        if (length == 0)
        {
            return 0;
        }

        using var context = _factory.CreateMeasurementContext(snapshot.Dpi);
        return context.MeasureText(
            snapshot.Text.AsSpan(start, length),
            GetFont(snapshot.DefaultStyle, snapshot.Dpi)).Width;
    }

    internal double[]? MeasureFastPathAdvances(TextLayoutRequestSnapshot snapshot, int start, int length)
    {
        start = Math.Clamp(start, 0, snapshot.Text.Length);
        length = Math.Clamp(length, 0, snapshot.Text.Length - start);
        if (length == 0)
        {
            return [];
        }

        using var context = _factory.CreateMeasurementContext(snapshot.Dpi);
        return context is ITextAdvanceSource advanceSource
            ? advanceSource.GetUtf16PrefixAdvances(
                snapshot.Text.AsSpan(start, length),
                GetFont(snapshot.DefaultStyle, snapshot.Dpi))
            : null;
    }

    private static List<ManagedTextSegment> MeasureFastPathSegments(
        IGraphicsContext context,
        string text,
        IFont font,
        out Size measured)
    {
        var segmentEnds = new List<int>(Math.Max(1, text.Length / FastPathSegmentLength + 1));
        int start = 0;
        while (start < text.Length)
        {
            int end = FindFastPathSegmentEnd(text, start);
            segmentEnds.Add(end);
            start = end;
        }

        var segments = new List<ManagedTextSegment>(segmentEnds.Count);
        double x = 0;
        double height = 0;
        start = 0;
        foreach (int end in segmentEnds)
        {
            var size = context.MeasureText(text.AsSpan(start, end - start), font);
            segments.Add(new ManagedTextSegment(start, end - start, x, Math.Max(0, size.Width)));
            x += Math.Max(0, size.Width);
            height = Math.Max(height, size.Height);
            start = end;
        }
        measured = new Size(x, height);
        return segments;
    }

    private static int FindFastPathSegmentEnd(string text, int start)
    {
        int target = Math.Min(text.Length, start + FastPathSegmentLength);
        if (target == text.Length)
        {
            return target;
        }
        if (char.IsAscii(text[target - 1]) && char.IsAscii(text[target]))
        {
            return target;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text, start);
        while (enumerator.MoveNext())
        {
            int boundary = enumerator.ElementIndex;
            if (boundary >= target)
            {
                return boundary;
            }
        }
        return text.Length;
    }

    private List<ManagedTextCluster> MeasureClusters(
        IGraphicsContext context,
        TextLayoutRequestSnapshot snapshot,
        int start,
        int length)
    {
        int end = checked(start + length);
        var boundaries = GetTextElementBoundaries(snapshot.Text, start, end);
        var clusters = new List<ManagedTextCluster>(boundaries.Count);

        for (int i = 0; i < boundaries.Count; i++)
        {
            int clusterStart = boundaries[i];
            int clusterEnd = i + 1 < boundaries.Count ? boundaries[i + 1] : end;
            int clusterLength = clusterEnd - clusterStart;
            var style = snapshot.GetStyle(clusterStart);
            var font = GetFont(style, snapshot.Dpi);

            if (snapshot.TryGetInline(clusterStart, out var inline))
            {
                var metrics = inline.Object.Measure();
                clusters.Add(new ManagedTextCluster(
                    clusterStart,
                    Math.Max(clusterLength, inline.Length),
                    0,
                    metrics.Width,
                    metrics.Height,
                    metrics.Baseline,
                    style,
                    font,
                    inline.Object,
                    ManagedTextClusterKind.Inline));
                int inlineEnd = checked(inline.Position + inline.Length);
                while (i + 1 < boundaries.Count && boundaries[i + 1] < inlineEnd)
                {
                    i++;
                }
                continue;
            }

            var span = snapshot.Text.AsSpan(clusterStart, clusterLength);
            if (span is ['\r'] or ['\n'] or ['\r', '\n'])
            {
                clusters.Add(new ManagedTextCluster(
                    clusterStart,
                    clusterLength,
                    0,
                    0,
                    font.Ascent + font.Descent,
                    font.Ascent,
                    style,
                    font,
                    null,
                    ManagedTextClusterKind.NewLine));
                continue;
            }

            if (span is ['\t'])
            {
                clusters.Add(new ManagedTextCluster(
                    clusterStart,
                    clusterLength,
                    0,
                    0,
                    font.Ascent + font.Descent,
                    font.Ascent,
                    style,
                    font,
                    null,
                    ManagedTextClusterKind.Tab));
                continue;
            }

            var measured = context.MeasureText(span, font);
            clusters.Add(new ManagedTextCluster(
                clusterStart,
                clusterLength,
                0,
                Math.Max(0, measured.Width + snapshot.Paragraph.LetterSpacing),
                Math.Max(font.Ascent + font.Descent, measured.Height),
                font.Ascent,
                style,
                font,
                null,
                ManagedTextClusterKind.Text));
        }

        ApplyBackendAdvances(context, snapshot, clusters);
        return clusters;
    }

    private static void ApplyBackendAdvances(
        IGraphicsContext context,
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextCluster> clusters)
    {
        if (context is not ITextAdvanceSource advanceSource)
        {
            return;
        }

        int index = 0;
        while (index < clusters.Count)
        {
            var first = clusters[index];
            if (first.Kind != ManagedTextClusterKind.Text)
            {
                index++;
                continue;
            }

            int endIndex = index + 1;
            while (endIndex < clusters.Count &&
                   clusters[endIndex].Kind == ManagedTextClusterKind.Text &&
                   clusters[endIndex].Style == first.Style &&
                   clusters[endIndex].Start == clusters[endIndex - 1].End)
            {
                endIndex++;
            }

            int textStart = first.Start;
            int textEnd = clusters[endIndex - 1].End;
            var cumulative = advanceSource.GetUtf16PrefixAdvances(
                snapshot.Text.AsSpan(textStart, textEnd - textStart),
                first.Font);
            double previous = 0;
            for (int clusterIndex = index; clusterIndex < endIndex; clusterIndex++)
            {
                var cluster = clusters[clusterIndex];
                int relativeEnd = cluster.End - textStart;
                double current = cumulative[relativeEnd - 1];
                cluster.Width = Math.Max(0, current - previous + snapshot.Paragraph.LetterSpacing);
                previous = current;
            }
            index = endIndex;
        }
    }

    private List<ManagedTextLine> AssembleLines(
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextCluster> clusters)
    {
        var lines = new List<ManagedTextLine>();
        double y = 0;
        int lineStart = 0;
        int index = 0;
        double maxWidth = NormalizeMaxWidth(snapshot.Paragraph.MaxWidth);

        while (index < clusters.Count)
        {
            int scan = index;
            int lastBreak = -1;
            double width = 0;
            bool explicitBreak = false;

            while (scan < clusters.Count)
            {
                var cluster = clusters[scan];
                if (cluster.Kind == ManagedTextClusterKind.NewLine)
                {
                    explicitBreak = true;
                    break;
                }

                double clusterWidth = cluster.Kind == ManagedTextClusterKind.Tab
                    ? GetTabWidth(snapshot.Paragraph, width, cluster.Font, snapshot.Dpi, context: null)
                    : cluster.Width;
                bool exceeds = snapshot.Paragraph.Wrapping != TextWrapping.NoWrap &&
                               !double.IsPositiveInfinity(maxWidth) &&
                               width + clusterWidth > maxWidth &&
                               scan > index;
                if (exceeds)
                {
                    if (snapshot.Paragraph.Wrapping == TextWrapping.Wrap && lastBreak >= index)
                    {
                        scan = lastBreak + 1;
                    }
                    else if (snapshot.Paragraph.Wrapping == TextWrapping.WrapWithOverflow && lastBreak < index)
                    {
                        width += clusterWidth;
                        scan++;
                        continue;
                    }

                    break;
                }

                cluster.Width = clusterWidth;
                width += clusterWidth;
                if (cluster.IsBreakOpportunity(snapshot.Text))
                {
                    lastBreak = scan;
                }
                scan++;
            }

            int contentEnd = scan;
            if (contentEnd == index && !explicitBreak && scan < clusters.Count)
            {
                contentEnd = ++scan;
            }

            var lineClusters = clusters.GetRange(index, contentEnd - index);
            var line = CreateLine(snapshot, lineClusters, y, explicitBreak ? clusters[scan].Length : 0, lineStart);
            lines.Add(line);
            y = line.Metrics.Bounds.Bottom + snapshot.Paragraph.LineSpacing;

            if (explicitBreak)
            {
                lineStart = clusters[scan].End;
                index = scan + 1;
            }
            else
            {
                lineStart = contentEnd < clusters.Count ? clusters[contentEnd].Start : snapshot.Text.Length;
                index = contentEnd;
            }
        }

        if (clusters.Count == 0 || clusters[^1].Kind == ManagedTextClusterKind.NewLine)
        {
            var font = GetFont(snapshot.DefaultStyle, snapshot.Dpi);
            double height = ResolveLineHeight(snapshot.Paragraph, font.Ascent + font.Descent, font.Ascent + font.Descent);
            lines.Add(new ManagedTextLine(
                new TextLayoutLineMetrics(snapshot.Text.Length, 0, 0, new Rect(0, y, 0, height), font.Ascent),
                []));
        }

        return lines;
    }

    private ManagedTextLine CreateLine(
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextCluster> clusters,
        double y,
        int newLineLength,
        int fallbackStart)
    {
        double width = clusters.Sum(static cluster => cluster.Width);
        double naturalHeight = clusters.Count == 0
            ? 0
            : clusters.Max(static cluster => cluster.Height);
        double baseline = clusters.Count == 0
            ? 0
            : clusters.Max(static cluster => cluster.Baseline);
        var defaultFont = GetFont(snapshot.DefaultStyle, snapshot.Dpi);
        double height = ResolveLineHeight(snapshot.Paragraph, defaultFont.Ascent + defaultFont.Descent, naturalHeight);
        if (baseline <= 0)
        {
            baseline = defaultFont.Ascent;
        }

        double x = ResolveLineX(snapshot.Paragraph, width);
        double cursor = x;
        foreach (var cluster in clusters)
        {
            cluster.X = cursor;
            cursor += cluster.Width;
        }

        int textStart = clusters.Count == 0 ? fallbackStart : clusters[0].Start;
        int textLength = clusters.Count == 0 ? 0 : clusters[^1].End - textStart;
        return new ManagedTextLine(
            new TextLayoutLineMetrics(textStart, textLength, newLineLength, new Rect(x, y, width, height), baseline),
            clusters);
    }

    internal IFont GetFont(TextRunStyle style, uint dpi)
    {
        var key = new FontKey(style.FontFamily, style.FontSize, style.Weight, style.Italic, dpi);
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = _factory.CreateFont(
                style.FontFamily,
                style.FontSize,
                dpi,
                style.Weight,
                style.Italic,
                style.Decoration.HasFlag(TextDecoration.Underline),
                style.Decoration.HasFlag(TextDecoration.Strikethrough));
            _fonts.Add(key, font);
        }

        return font;
    }

    private static List<int> GetTextElementBoundaries(string text, int start, int end)
    {
        if (start == end)
        {
            return [];
        }

        var result = new List<int>();
        var enumerator = StringInfo.GetTextElementEnumerator(text, start);
        while (enumerator.MoveNext())
        {
            int index = enumerator.ElementIndex;
            if (index >= end)
            {
                break;
            }

            if (index > start && text[index - 1] == '\r' && text[index] == '\n')
            {
                continue;
            }

            result.Add(index);
        }

        return result;
    }

    private static double NormalizeMaxWidth(double width)
        => double.IsNaN(width) || width <= 0 || double.IsPositiveInfinity(width)
            ? double.PositiveInfinity
            : width;

    private static double ResolveLineX(TextParagraphStyle paragraph, double width)
    {
        double maxWidth = NormalizeMaxWidth(paragraph.MaxWidth);
        if (double.IsPositiveInfinity(maxWidth))
        {
            return 0;
        }

        return paragraph.Alignment switch
        {
            TextAlignment.Center => Math.Max(0, (maxWidth - width) * 0.5),
            TextAlignment.Right => Math.Max(0, maxWidth - width),
            _ => 0
        };
    }

    private static double ResolveLineHeight(TextParagraphStyle paragraph, double fontHeight, double measuredHeight)
        => paragraph.LineHeight is > 0
            ? paragraph.LineHeight.Value
            : Math.Max(fontHeight, measuredHeight);

    private double GetTabWidth(TextParagraphStyle paragraph, double x, IFont font, uint dpi, IGraphicsContext? context)
    {
        foreach (double stop in paragraph.TabStops)
        {
            if (stop > x)
            {
                return stop - x;
            }
        }

        bool ownsContext = context is null;
        context ??= _factory.CreateMeasurementContext(dpi);
        try
        {
            double space = Math.Max(1, context.MeasureText(" ", font).Width);
            double interval = space * 4;
            return interval - x % interval;
        }
        finally
        {
            if (ownsContext)
            {
                context.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cache.Dispose();
        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }
        _fonts.Clear();
    }

    private readonly record struct FontKey(string Family, double Size, FontWeight Weight, bool Italic, uint Dpi);
}

internal sealed class TextLayoutRequestSnapshot
{
    private string? _contentKey;
    private string? _ownerKey;

    private TextLayoutRequestSnapshot(
        string text,
        uint dpi,
        TextParagraphStyle paragraph,
        TextRunStyle defaultStyle,
        GeometryStyleRun[] runs,
        InlineRun[] inlines,
        TextFidelity fidelity,
        long revision,
        bool transient)
    {
        Text = text;
        Dpi = dpi;
        Paragraph = paragraph;
        DefaultStyle = defaultStyle;
        Runs = runs;
        Inlines = inlines;
        Fidelity = fidelity;
        Revision = revision;
        Transient = transient;
    }

    public string Text { get; }
    public uint Dpi { get; }
    public TextParagraphStyle Paragraph { get; }
    public TextRunStyle DefaultStyle { get; }
    public GeometryStyleRun[] Runs { get; }
    public InlineRun[] Inlines { get; }
    public TextFidelity Fidelity { get; }
    public long Revision { get; }
    public bool Transient { get; }
    public string ContentKey => _contentKey ??= CreateCacheKey(includeText: true);
    public string OwnerKey => _ownerKey ??= CreateCacheKey(includeText: false);
    internal bool HasMaterializedContentKey => _contentKey is not null;

    public static TextLayoutRequestSnapshot Create(TextLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Paragraph);
        ValidateStyle(request.DefaultStyle, nameof(request.DefaultStyle));

        string text = request.Text.ToString();
        uint dpi = request.Dpi == 0 ? 96 : request.Dpi;
        var runs = request.Runs?.ToArray() ?? [];
        var inlines = request.Inlines?.ToArray() ?? [];
        HashSet<int>? textElementBoundaries = null;
        if (runs.Length > 0 || inlines.Length > 0)
        {
            textElementBoundaries = new HashSet<int>(StringInfo.ParseCombiningCharacters(text)) { text.Length };
        }
        Array.Sort(runs, static (left, right) => left.Start.CompareTo(right.Start));
        int previousEnd = 0;
        foreach (var run in runs)
        {
            ValidateRange(run.Start, run.Length, text.Length, nameof(request.Runs));
            ValidateStyle(run.Style, nameof(request.Runs));
            ValidateTextElementRange(run.Start, run.Length, textElementBoundaries!, nameof(request.Runs));
            if (run.Start < previousEnd)
            {
                throw new ArgumentException("Geometry style runs must not overlap.", nameof(request));
            }
            previousEnd = run.End;
        }

        Array.Sort(inlines, static (left, right) => left.Position.CompareTo(right.Position));
        previousEnd = 0;
        foreach (var inline in inlines)
        {
            ArgumentNullException.ThrowIfNull(inline.Object);
            ValidateRange(inline.Position, inline.Length, text.Length, nameof(request.Inlines));
            ValidateTextElementRange(inline.Position, inline.Length, textElementBoundaries!, nameof(request.Inlines));
            if (inline.Length <= 0 || inline.Position < previousEnd)
            {
                throw new ArgumentException("Inline runs must be non-empty and must not overlap.", nameof(request));
            }
            previousEnd = checked(inline.Position + inline.Length);
        }

        var paragraph = request.Paragraph with
        {
            TabStops = request.Paragraph.TabStops?.ToArray() ?? [],
            Culture = request.Paragraph.Culture ?? CultureInfo.CurrentUICulture
        };
        if (paragraph.LineHeight is <= 0 || paragraph.LineSpacing < 0 || double.IsNaN(paragraph.MaxWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Paragraph metrics must be finite and non-negative.");
        }

        return new TextLayoutRequestSnapshot(
            text,
            dpi,
            paragraph,
            request.DefaultStyle,
            runs,
            inlines,
            request.Fidelity,
            request.Revision,
            request.Transient);
    }

    public TextRunStyle GetStyle(int textIndex)
    {
        foreach (var run in Runs)
        {
            if (textIndex >= run.Start && textIndex < run.End)
            {
                return run.Style;
            }
            if (run.Start > textIndex)
            {
                break;
            }
        }
        return DefaultStyle;
    }

    public bool TryGetInline(int position, out InlineRun inline)
    {
        foreach (var candidate in Inlines)
        {
            if (candidate.Position == position)
            {
                inline = candidate;
                return true;
            }
            if (candidate.Position > position)
            {
                break;
            }
        }
        inline = default;
        return false;
    }

    private string CreateCacheKey(bool includeText)
    {
        var builder = new StringBuilder(includeText ? Text.Length + 128 : 128);
        if (includeText)
        {
            builder.Append(Text);
        }
        builder.Append('\u001f').Append(Dpi).Append('\u001f').Append((int)Fidelity)
            .Append('\u001f').Append(Paragraph.MaxWidth.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append(Paragraph.MaxHeight.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append((int)Paragraph.Wrapping).Append('\u001f').Append((int)Paragraph.Trimming)
            .Append('\u001f').Append((int)Paragraph.Alignment).Append('\u001f').Append((int)Paragraph.FlowDirection)
            .Append('\u001f').Append(Paragraph.Culture.Name).Append('\u001f').Append(Paragraph.Language)
            .Append('\u001f').Append(Paragraph.LineHeight?.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append(Paragraph.LineSpacing.ToString("R", CultureInfo.InvariantCulture))
            .Append('\u001f').Append(Paragraph.LetterSpacing.ToString("R", CultureInfo.InvariantCulture));
        AppendStyle(builder, DefaultStyle);
        foreach (double tab in Paragraph.TabStops)
        {
            builder.Append('\u001e').Append(tab.ToString("R", CultureInfo.InvariantCulture));
        }
        foreach (var run in Runs)
        {
            builder.Append('\u001d').Append(run.Start).Append(':').Append(run.Length);
            AppendStyle(builder, run.Style);
        }
        foreach (var inline in Inlines)
        {
            builder.Append('\u001c').Append(inline.Position).Append(':').Append(inline.Length)
                .Append(':').Append(RuntimeHelpers.GetHashCode(inline.Object));
        }
        return builder.ToString();
    }

    private static void AppendStyle(StringBuilder builder, TextRunStyle style)
        => builder.Append('\u001b').Append(style.FontFamily)
            .Append(':').Append(style.FontSize.ToString("R", CultureInfo.InvariantCulture))
            .Append(':').Append((int)style.Weight).Append(':').Append(style.Italic)
            .Append(':').Append((int)style.Decoration).Append(':').Append(style.Culture?.Name)
            .Append(':').Append(style.Language);

    private static void ValidateStyle(TextRunStyle style, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(style.FontFamily) || style.FontSize <= 0 || double.IsNaN(style.FontSize))
        {
            throw new ArgumentException("Text styles require a font family and positive font size.", parameterName);
        }
    }

    private static void ValidateRange(int start, int length, int textLength, string parameterName)
    {
        if (start < 0 || length < 0 || start > textLength - length)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateTextElementRange(
        int start,
        int length,
        HashSet<int> boundaries,
        string parameterName)
    {
        if (!boundaries.Contains(start) || !boundaries.Contains(checked(start + length)))
        {
            throw new ArgumentException(
                "Text ranges must start and end at Unicode text-element boundaries.",
                parameterName);
        }
    }
}

internal enum ManagedTextClusterKind { Text, Tab, NewLine, Inline }

internal sealed class ManagedTextCluster(
    int start,
    int length,
    double x,
    double width,
    double height,
    double baseline,
    TextRunStyle style,
    IFont font,
    IInlineTextObject? inline,
    ManagedTextClusterKind kind)
{
    public int Start { get; } = start;
    public int Length { get; } = length;
    public int End => checked(Start + Length);
    public double X { get; set; } = x;
    public double Width { get; set; } = width;
    public double Height { get; } = height;
    public double Baseline { get; } = baseline;
    public TextRunStyle Style { get; } = style;
    public IFont Font { get; } = font;
    public IInlineTextObject? Inline { get; } = inline;
    public ManagedTextClusterKind Kind { get; } = kind;

    public bool IsBreakOpportunity(string text)
        => Kind == ManagedTextClusterKind.Text &&
           Length > 0 &&
           char.IsWhiteSpace(text, Start);
}

internal readonly record struct ManagedTextSegment(int Start, int Length, double X, double Width)
{
    public int End => checked(Start + Length);
}

internal sealed class ManagedTextLine(
    TextLayoutLineMetrics metrics,
    List<ManagedTextCluster>? clusters,
    IReadOnlyList<ManagedTextSegment>? fastSegments = null)
{
    public TextLayoutLineMetrics Metrics { get; } = metrics;
    public List<ManagedTextCluster>? Clusters { get; set; } = clusters;
    public IReadOnlyList<ManagedTextSegment>? FastSegments { get; } = fastSegments;
}

internal sealed class ManagedTextLayoutCache : ITextLayoutCache, IDisposable
{
    private const int ContentCapacity = 256;
    private readonly ManagedTextEngine _engine;
    private readonly Dictionary<string, ManagedTextLayout> _content = [];
    private readonly Queue<string> _contentOrder = [];
    private readonly ConditionalWeakTable<object, OwnerEntry> _owners = new();
    private int _ownerCount;

    public ManagedTextLayoutCache(ManagedTextEngine engine) => _engine = engine;

    public int Count => _content.Count + _ownerCount;

    public ManagedTextLayout GetOrCreate(
        TextLayoutRequestSnapshot snapshot,
        TextLayoutCachePolicy policy,
        object? owner)
    {
        if (policy == TextLayoutCachePolicy.Owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (_owners.TryGetValue(owner, out var entry) &&
                entry.Revision == snapshot.Revision &&
                entry.OwnerKey == snapshot.OwnerKey)
            {
                return entry.Layout;
            }

            var layout = _engine.CreateLayoutCore(snapshot);
            if (entry is null)
            {
                _owners.Add(owner, new OwnerEntry(snapshot.Revision, snapshot.OwnerKey, layout));
                _ownerCount++;
            }
            else
            {
                entry.Revision = snapshot.Revision;
                entry.OwnerKey = snapshot.OwnerKey;
                entry.Layout = layout;
            }
            return layout;
        }

        if (snapshot.Inlines.Length > 0)
        {
            throw new ArgumentException("Layouts containing inline objects require owner caching.", nameof(snapshot));
        }
        if (_content.TryGetValue(snapshot.ContentKey, out var cached))
        {
            return cached;
        }

        var created = _engine.CreateLayoutCore(snapshot);
        _content.Add(snapshot.ContentKey, created);
        _contentOrder.Enqueue(snapshot.ContentKey);
        while (_content.Count > ContentCapacity && _contentOrder.TryDequeue(out string? oldest))
        {
            _content.Remove(oldest);
        }
        return created;
    }

    public void ReleaseOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owners.Remove(owner))
        {
            _ownerCount--;
        }
    }

    public void Trim()
    {
        _content.Clear();
        _contentOrder.Clear();
    }

    public void Dispose() => Trim();

    private sealed class OwnerEntry(long revision, string ownerKey, ManagedTextLayout layout)
    {
        public long Revision { get; set; } = revision;
        public string OwnerKey { get; set; } = ownerKey;
        public ManagedTextLayout Layout { get; set; } = layout;
    }
}
