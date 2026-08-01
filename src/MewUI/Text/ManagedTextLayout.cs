using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal interface IManagedTextLayoutData
{
    TextLayoutRequestSnapshot Snapshot { get; }
    IReadOnlyList<ManagedTextLine> ManagedLines { get; }
}

internal sealed class ManagedTextLayout : ITextLayout, IManagedTextLayoutData
{
    private readonly ManagedTextEngine _engine;
    private readonly List<ManagedTextLine> _lines;
    private readonly IReadOnlyList<TextLayoutLineMetrics> _lineMetrics;
    private int[]? _fastCaretBoundaries;

    public ManagedTextLayout(
        ManagedTextEngine engine,
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextLine> lines,
        Size measuredSize,
        bool isFastPath)
    {
        _engine = engine;
        Snapshot = snapshot;
        _lines = lines;
        _lineMetrics = lines.Select(static line => line.Metrics).ToArray();
        MeasuredSize = measuredSize;
        ContentHeight = lines.Count == 0 ? 0 : lines[^1].Metrics.Bounds.Bottom;
        IsFastPath = isFastPath;
    }

    public TextLayoutRequestSnapshot Snapshot { get; }

    public IReadOnlyList<ManagedTextLine> ManagedLines => _lines;

    public Size MeasuredSize { get; }

    public double ContentHeight { get; }

    public IReadOnlyList<TextLayoutLineMetrics> Lines => _lineMetrics;

    internal bool IsFastPath { get; }

    internal IFont GetDefaultFont() => _engine.GetFont(Snapshot.DefaultStyle, Snapshot.Dpi);

    internal bool HasMaterializedClusters
        => _lines.Any(static line => line.Clusters is not null);

    public CharacterHit HitTestPoint(Point point)
    {
        if (_lines.Count == 0)
        {
            return default;
        }

        int lineIndex = FindLineByY(point.Y);
        var line = _lines[lineIndex];
        if (IsFastPath && line.Clusters is null)
        {
            return HitTestFastPath(line, point.X);
        }
        var clusters = EnsureClusters(line);
        if (clusters.Count == 0)
        {
            return new CharacterHit(line.Metrics.TextStart, 0);
        }

        if (point.X <= clusters[0].X)
        {
            return new CharacterHit(clusters[0].Start, 0);
        }

        foreach (var cluster in clusters)
        {
            if (point.X <= cluster.X + cluster.Width)
            {
                return point.X < cluster.X + cluster.Width * 0.5
                    ? new CharacterHit(cluster.Start, 0)
                    : new CharacterHit(cluster.Start, cluster.Length);
            }
        }

        var last = clusters[^1];
        return new CharacterHit(last.Start, last.Length);
    }

    public Rect GetCaretBounds(CharacterHit hit)
    {
        int insertion = Math.Clamp(hit.InsertionIndex, 0, Snapshot.Text.Length);
        var line = FindLineByInsertion(insertion);
        if (IsFastPath && line.Clusters is null)
        {
            return new Rect(
                GetFastPathX(line, insertion),
                line.Metrics.Bounds.Y,
                1,
                line.Metrics.Bounds.Height);
        }
        var clusters = EnsureClusters(line);
        double x = line.Metrics.Bounds.X;

        foreach (var cluster in clusters)
        {
            if (insertion <= cluster.Start)
            {
                x = cluster.X;
                break;
            }
            if (insertion <= cluster.End)
            {
                x = insertion == cluster.Start ? cluster.X : cluster.X + cluster.Width;
                break;
            }
            x = cluster.X + cluster.Width;
        }

        return new Rect(x, line.Metrics.Bounds.Y, 1, line.Metrics.Bounds.Height);
    }

    public CharacterHit GetNextLogicalCaret(CharacterHit from, LogicalDirection direction, CaretMode mode)
    {
        int insertion = Math.Clamp(from.InsertionIndex, 0, Snapshot.Text.Length);
        if (mode == CaretMode.CodeUnit)
        {
            int next = direction == LogicalDirection.Forward
                ? Math.Min(Snapshot.Text.Length, insertion + 1)
                : Math.Max(0, insertion - 1);
            return new CharacterHit(next, 0);
        }

        IReadOnlyList<int> boundaries = IsFastPath && !_lines.Any(static line => line.Clusters is not null)
            ? GetFastCaretBoundaries()
            : GetCaretBoundaries();
        if (direction == LogicalDirection.Forward)
        {
            foreach (int boundary in boundaries)
            {
                if (boundary > insertion)
                {
                    return new CharacterHit(boundary, 0);
                }
            }
            return new CharacterHit(Snapshot.Text.Length, 0);
        }

        for (int i = boundaries.Count - 1; i >= 0; i--)
        {
            if (boundaries[i] < insertion)
            {
                return new CharacterHit(boundaries[i], 0);
            }
        }
        return default;
    }

    public CharacterHit GetNextVisualCaret(CharacterHit from, VisualDirection direction, CaretMode mode)
    {
        if (direction == VisualDirection.Left)
        {
            return GetNextLogicalCaret(from, LogicalDirection.Backward, mode);
        }
        if (direction == VisualDirection.Right)
        {
            return GetNextLogicalCaret(from, LogicalDirection.Forward, mode);
        }

        var caret = GetCaretBounds(from);
        int currentLine = FindLineIndexByInsertion(Math.Clamp(from.InsertionIndex, 0, Snapshot.Text.Length));
        int targetLine = direction == VisualDirection.Up ? currentLine - 1 : currentLine + 1;
        if (targetLine < 0 || targetLine >= _lines.Count)
        {
            return from;
        }

        var target = _lines[targetLine].Metrics.Bounds;
        return HitTestPoint(new Point(caret.X, target.Y + target.Height * 0.5));
    }

    public void GetRangeBounds(int start, int length, IList<Rect> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (start < 0 || length < 0 || start > Snapshot.Text.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        if (length == 0)
        {
            return;
        }

        if (IsFastPath && !_lines.Any(static line => line.Clusters is not null))
        {
            var line = _lines[0];
            double left = GetFastPathX(line, start);
            double right = GetFastPathX(line, start + length);
            output.Add(new Rect(
                Math.Min(left, right),
                line.Metrics.Bounds.Y,
                Math.Abs(right - left),
                line.Metrics.Bounds.Height));
            return;
        }

        int end = start + length;
        foreach (var line in _lines)
        {
            double left = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            foreach (var cluster in EnsureClusters(line))
            {
                if (cluster.End <= start || cluster.Start >= end)
                {
                    continue;
                }
                left = Math.Min(left, cluster.X);
                right = Math.Max(right, cluster.X + cluster.Width);
            }

            if (!double.IsPositiveInfinity(left))
            {
                output.Add(new Rect(left, line.Metrics.Bounds.Y, Math.Max(0, right - left), line.Metrics.Bounds.Height));
            }
        }
    }

    internal List<ManagedTextCluster> EnsureClusters(ManagedTextLine line)
    {
        if (line.Clusters is not null)
        {
            return line.Clusters;
        }

        lock (line)
        {
            if (line.Clusters is not null)
            {
                return line.Clusters;
            }

            var clusters = _engine.MeasureClusters(Snapshot, line.Metrics.TextStart, line.Metrics.TextLength);
            double naturalWidth = clusters.Sum(static cluster => cluster.Width);
            double scale = naturalWidth > 0 ? line.Metrics.Bounds.Width / naturalWidth : 1;
            double x = line.Metrics.Bounds.X;
            foreach (var cluster in clusters)
            {
                cluster.X = x;
                cluster.Width *= scale;
                x += cluster.Width;
            }
            line.Clusters = clusters;
            return clusters;
        }
    }

    private int FindLineByY(double y)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var bounds = _lines[i].Metrics.Bounds;
            if (y < bounds.Bottom)
            {
                return i;
            }
        }
        return _lines.Count - 1;
    }

    private ManagedTextLine FindLineByInsertion(int insertion)
        => _lines[FindLineIndexByInsertion(insertion)];

    private int FindLineIndexByInsertion(int insertion)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var metrics = _lines[i].Metrics;
            int lineEnd = metrics.TextEnd + metrics.NewLineLength;
            if (insertion <= lineEnd || i == _lines.Count - 1)
            {
                return i;
            }
        }
        return _lines.Count - 1;
    }

    private List<int> GetCaretBoundaries()
    {
        var boundaries = new List<int> { 0 };
        foreach (var line in _lines)
        {
            foreach (var cluster in EnsureClusters(line))
            {
                if (boundaries[^1] != cluster.Start)
                {
                    boundaries.Add(cluster.Start);
                }
                if (boundaries[^1] != cluster.End)
                {
                    boundaries.Add(cluster.End);
                }
            }
            int lineEnd = line.Metrics.TextEnd + line.Metrics.NewLineLength;
            if (boundaries[^1] != lineEnd)
            {
                boundaries.Add(lineEnd);
            }
        }
        if (boundaries[^1] != Snapshot.Text.Length)
        {
            boundaries.Add(Snapshot.Text.Length);
        }
        return boundaries;
    }

    private CharacterHit HitTestFastPath(ManagedTextLine line, double x)
    {
        var bounds = line.Metrics.Bounds;
        if (x <= bounds.X)
        {
            return default;
        }
        if (x >= bounds.Right)
        {
            return new CharacterHit(Snapshot.Text.Length, 0);
        }

        var segments = line.FastSegments!;
        ManagedTextSegment segment = segments[^1];
        foreach (var candidate in segments)
        {
            if (x <= candidate.X + candidate.Width)
            {
                segment = candidate;
                break;
            }
        }
        int[] boundaries = GetSegmentCaretBoundaries(segment);
        int low = 0;
        int high = boundaries.Length - 1;
        while (high - low > 1)
        {
            int middle = low + (high - low) / 2;
            double middleX = GetFastPathX(line, boundaries[middle]);
            if (x < middleX)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        double leadingX = GetFastPathX(line, boundaries[low]);
        double trailingX = GetFastPathX(line, boundaries[high]);
        return x < leadingX + (trailingX - leadingX) * 0.5
            ? new CharacterHit(boundaries[low], 0)
            : new CharacterHit(boundaries[high], 0);
    }

    private double GetFastPathX(ManagedTextLine line, int insertion)
    {
        insertion = Math.Clamp(insertion, 0, Snapshot.Text.Length);
        if (insertion == 0)
        {
            return line.Metrics.Bounds.X;
        }
        if (insertion == Snapshot.Text.Length)
        {
            return line.Metrics.Bounds.Right;
        }
        foreach (var segment in line.FastSegments!)
        {
            if (insertion > segment.End)
            {
                continue;
            }
            if (insertion == segment.End)
            {
                return segment.X + segment.Width;
            }
            return segment.X + _engine.MeasureFastPathRange(
                Snapshot,
                segment.Start,
                insertion - segment.Start);
        }
        return line.Metrics.Bounds.Right;
    }

    private int[] GetSegmentCaretBoundaries(ManagedTextSegment segment)
    {
        var boundaries = new List<int>();
        var enumerator = StringInfo.GetTextElementEnumerator(Snapshot.Text, segment.Start);
        while (enumerator.MoveNext())
        {
            int boundary = enumerator.ElementIndex;
            if (boundary > segment.End)
            {
                break;
            }
            boundaries.Add(boundary);
            if (boundary == segment.End)
            {
                break;
            }
        }
        if (boundaries.Count == 0 || boundaries[^1] != segment.End)
        {
            boundaries.Add(segment.End);
        }
        return boundaries.ToArray();
    }

    private int[] GetFastCaretBoundaries()
    {
        if (_fastCaretBoundaries is not null)
        {
            return _fastCaretBoundaries;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(Snapshot.Text);
        if (starts.Length == 0)
        {
            return _fastCaretBoundaries = [0];
        }
        if (starts[^1] == Snapshot.Text.Length)
        {
            return _fastCaretBoundaries = starts;
        }

        var boundaries = new int[starts.Length + 1];
        starts.CopyTo(boundaries, 0);
        boundaries[^1] = Snapshot.Text.Length;
        return _fastCaretBoundaries = boundaries;
    }
}
