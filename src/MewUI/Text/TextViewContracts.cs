namespace Aprillz.MewUI.Text;

public interface ITextSource
{
    int TextLength { get; }
    long Version { get; }
    char GetCharAt(int offset);
    string GetText(int offset, int length);
}

public interface IReadOnlyDocumentLine
{
    int LineNumber { get; }
    int Offset { get; }
    int Length { get; }
    int TotalLength { get; }
    string Delimiter { get; }
}

public interface IReadOnlyTextDocument : ITextSource
{
    int LineCount { get; }
    IReadOnlyDocumentLine GetLineByNumber(int lineNumber);
    IReadOnlyDocumentLine GetLineByOffset(int offset);
    int GetOffset(int line, int column);
    TextLocation GetLocation(int offset);
}

public readonly record struct TextLocation(int Line, int Column);

public readonly record struct LogicalTextLine(
    int LineNumber,
    int Offset,
    int Length,
    int TotalLength);

public sealed class VisualTextLine
{
    internal VisualTextLine(
        int logicalStart,
        int logicalLength,
        int visualRow,
        Rect bounds,
        double baseline,
        ITextLayout layout,
        int layoutLineIndex)
    {
        LogicalStart = logicalStart;
        LogicalLength = logicalLength;
        VisualRow = visualRow;
        Bounds = bounds;
        Baseline = baseline;
        Layout = layout;
        LayoutLineIndex = layoutLineIndex;
    }

    public int LogicalStart { get; }
    public int LogicalLength { get; }
    public int VisualRow { get; }
    public Rect Bounds { get; internal set; }
    public double Baseline { get; }
    public ITextLayout Layout { get; }
    public int LayoutLineIndex { get; }
}

public sealed class TextLineLayout
{
    private readonly ITextLayout _layout;
    private readonly List<VisualTextLine> _visualLines;

    internal TextLineLayout(
        LogicalTextLine logicalLine,
        ITextLayout layout,
        double documentX,
        double documentY,
        ITextOffsetMap offsetMap,
        IReadOnlyList<TextPaintSpan> paintSpans,
        IReadOnlyList<ITextAdornment> adornments,
        int visualRowOffset = 0)
    {
        LogicalLine = logicalLine;
        _layout = layout;
        OffsetMap = offsetMap;
        PaintSpans = paintSpans;
        Adornments = adornments;
        DocumentX = documentX;
        DocumentY = documentY;
        _visualLines = new List<VisualTextLine>(layout.Lines.Count);
        for (int i = 0; i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i];
            _visualLines.Add(new VisualTextLine(
                line.TextStart,
                line.TextLength,
                visualRowOffset + i,
                new Rect(documentX + line.Bounds.X, documentY + line.Bounds.Y, line.Bounds.Width, line.Bounds.Height),
                line.Baseline,
                layout,
                i));
        }
    }

    public LogicalTextLine LogicalLine { get; }
    public IReadOnlyList<VisualTextLine> VisualLines => _visualLines;
    public double Height => _layout.ContentHeight;
    public ITextOffsetMap OffsetMap { get; }
    public IReadOnlyList<TextPaintSpan> PaintSpans { get; }
    public IReadOnlyList<ITextAdornment> Adornments { get; }
    public double DocumentX { get; private set; }
    public double DocumentY { get; private set; }

    public CharacterHit HitTest(Point lineLocalPoint) => _layout.HitTestPoint(lineLocalPoint);

    public Rect GetCaretBounds(CharacterHit hit) => _layout.GetCaretBounds(hit);

    public void GetRangeBounds(TextRange range, IList<Rect> output)
        => _layout.GetRangeBounds(range.Start, range.Length, output);

    internal CharacterHit HitTestDocument(Point documentPoint)
        => _layout.HitTestPoint(new Point(documentPoint.X - DocumentX, documentPoint.Y));

    internal Rect GetDocumentCaretBounds(CharacterHit hit)
    {
        var bounds = _layout.GetCaretBounds(hit);
        return new Rect(DocumentX + bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>Draws the whole line. Hosts that interleave viewport renderers call the two passes instead.</summary>
    public void Draw(ITextRenderContext context, Point origin, in TextDrawOptions options)
    {
        DrawBackground(context, origin, in options);
        DrawForeground(context, origin, in options);
    }

    /// <summary>First pass: background adornments, paint-span backgrounds, then the selection layer.</summary>
    public void DrawBackground(ITextRenderContext context, Point origin, in TextDrawOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        var documentOrigin = new Point(origin.X + DocumentX, origin.Y);
        DrawAdornments(context, documentOrigin, TextAdornmentLayer.Background);
        context.DrawBackground(_layout, documentOrigin, Combine(in options));
        DrawAdornments(context, documentOrigin, TextAdornmentLayer.Selection);
    }

    /// <summary>Second pass: glyphs and the adornments that sit above them.</summary>
    public void DrawForeground(ITextRenderContext context, Point origin, in TextDrawOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        var documentOrigin = new Point(origin.X + DocumentX, origin.Y);
        context.DrawForeground(_layout, documentOrigin, Combine(in options));
        DrawAdornments(context, documentOrigin, TextAdornmentLayer.Text);
    }

    private TextDrawOptions Combine(in TextDrawOptions options)
    {
        TextDrawOptions effective = options;
        if (PaintSpans.Count > 0)
        {
            if (options.PaintSpans.IsEmpty)
            {
                effective = options with { PaintSpans = PaintSpans.ToArray() };
            }
            else
            {
                var combined = new TextPaintSpan[PaintSpans.Count + options.PaintSpans.Length];
                for (int i = 0; i < PaintSpans.Count; i++)
                {
                    combined[i] = PaintSpans[i];
                }
                options.PaintSpans.Span.CopyTo(combined.AsSpan(PaintSpans.Count));
                effective = options with { PaintSpans = combined };
            }
        }
        return effective;
    }

    /// <summary>Draws the adornments that belong above the caret; the host calls this after painting its caret.</summary>
    public void DrawCaretLayer(ITextRenderContext context, Point origin)
    {
        ArgumentNullException.ThrowIfNull(context);
        DrawAdornments(context, new Point(origin.X + DocumentX, origin.Y), TextAdornmentLayer.Caret);
    }

    public int MapProjectedOffsetToSource(int projectedOffset)
        => OffsetMap.MapToSource(projectedOffset);

    public int MapSourceOffsetToProjected(int sourceOffset)
        => OffsetMap.MapFromSource(sourceOffset);

    private void DrawAdornments(ITextRenderContext context, Point origin, TextAdornmentLayer layer)
    {
        foreach (var adornment in Adornments)
        {
            if (adornment.Layer == layer)
            {
                adornment.Draw(context, this, origin);
            }
        }
    }

    internal void SetDocumentPosition(double documentX, double documentY)
    {
        DocumentX = documentX;
        DocumentY = documentY;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            var source = _layout.Lines[i].Bounds;
            _visualLines[i].Bounds = new Rect(
                documentX + source.X,
                documentY + source.Y,
                source.Width,
                source.Height);
        }
    }
}

public readonly record struct TextViewport(
    double Width,
    double Height,
    double HorizontalOffset = 0,
    double VerticalOffset = 0)
{
    public Rect DocumentBounds => new(HorizontalOffset, VerticalOffset, Width, Height);
}

public readonly record struct TextChange(int Offset, int RemovedLength, int InsertedLength);

public readonly record struct TextViewHit(
    int DocumentOffset,
    int LineNumber,
    int VisualRow,
    CharacterHit LineHit);

public interface ITextViewLayout : IDisposable
{
    TextViewport Viewport { get; }
    IReadOnlyList<TextLineLayout> MaterializedLines { get; }
    double ExtentWidth { get; }
    double ExtentHeight { get; }
    void SetViewport(TextViewport viewport);
    void Invalidate(TextChange change);
    TextViewHit HitTest(Point viewportPoint);
    Rect GetCaretBounds(int documentOffset);
}
