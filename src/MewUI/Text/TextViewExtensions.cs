namespace Aprillz.MewUI.Text;

/// <summary>Classification input. <see cref="Text"/> is the projected display text; <see cref="OffsetMap"/> converts between its offsets and source document offsets.</summary>
public readonly record struct TextClassificationContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text,
    ITextOffsetMap OffsetMap);

public interface ITextClassifier
{
    void Classify(in TextClassificationContext context, IList<TextPaintSpan> output);
}

/// <summary>Transform input. <see cref="Text"/> is the projected display text; <see cref="OffsetMap"/> converts between its offsets and source document offsets.</summary>
public readonly record struct TextLineTransformContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text,
    TextRunStyle DefaultStyle,
    ITextOffsetMap OffsetMap);

public interface ITextLineTransformer
{
    void Transform(
        in TextLineTransformContext context,
        IList<GeometryStyleRun> geometryRuns,
        IList<InlineRun> inlines);
}

/// <summary>Element generation input. <see cref="Text"/> is the projected display text; <see cref="OffsetMap"/> converts between its offsets and source document offsets.</summary>
public readonly record struct TextElementContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text,
    ITextOffsetMap OffsetMap);

public interface ITextElementGenerator
{
    void Generate(in TextElementContext context, IList<InlineRun> output);
}

/// <summary>
/// Anchor an adornment draws at. Every adornment paints under the content of its own layer, so the
/// layer names what it sits beneath rather than what it covers.
/// </summary>
public enum TextAdornmentLayer
{
    /// <summary>Under the line backgrounds, the bottom of the stack.</summary>
    Background,

    /// <summary>Under the selection highlight.</summary>
    Selection,

    /// <summary>Under the glyphs.</summary>
    Text,

    /// <summary>Under the caret.</summary>
    Caret
}

public interface ITextOffsetMap
{
    int MapToSource(int projectedOffset);
    int MapFromSource(int sourceOffset);
}

public sealed class IdentityTextOffsetMap : ITextOffsetMap
{
    public static IdentityTextOffsetMap Instance { get; } = new();

    private IdentityTextOffsetMap() { }

    public int MapToSource(int projectedOffset) => projectedOffset;
    public int MapFromSource(int sourceOffset) => sourceOffset;
}

internal sealed class ComposedTextOffsetMap(ITextOffsetMap sourceMap, ITextOffsetMap projectedMap) : ITextOffsetMap
{
    public int MapToSource(int projectedOffset)
        => sourceMap.MapToSource(projectedMap.MapToSource(projectedOffset));

    public int MapFromSource(int sourceOffset)
        => projectedMap.MapFromSource(sourceMap.MapFromSource(sourceOffset));
}

public readonly record struct TextProjectionContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> SourceText);

public readonly record struct ProjectedText(ReadOnlyMemory<char> Text, ITextOffsetMap OffsetMap);

public interface ITextProjection
{
    ProjectedText Project(in TextProjectionContext context);
}

/// <summary>Removes complete logical lines from the visual text surface.</summary>
public interface ITextLineCollapser
{
    bool IsCollapsed(LogicalTextLine line);
}

public sealed class TextViewExtensionPipeline
{
    public long Revision { get; set; }
    /// <summary>Run in registration order; where paint spans overlap, the later registration wins.</summary>
    public IList<ITextClassifier> Classifiers { get; } = new List<ITextClassifier>();
    public IList<ITextLineTransformer> Transformers { get; } = new List<ITextLineTransformer>();
    public IList<ITextElementGenerator> ElementGenerators { get; } = new List<ITextElementGenerator>();
    public IList<ITextProjection> Projections { get; } = new List<ITextProjection>();
    public IList<ITextLineCollapser> LineCollapsers { get; } = new List<ITextLineCollapser>();
}
