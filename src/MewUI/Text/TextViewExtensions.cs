namespace Aprillz.MewUI.Text;

public readonly record struct TextClassificationContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text);

public interface ITextClassifier
{
    void Classify(in TextClassificationContext context, IList<TextPaintSpan> output);
}

public readonly record struct TextLineTransformContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text,
    TextRunStyle DefaultStyle);

public interface ITextLineTransformer
{
    void Transform(
        in TextLineTransformContext context,
        IList<GeometryStyleRun> geometryRuns,
        IList<InlineRun> inlines);
}

public readonly record struct TextElementContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text);

public interface ITextElementGenerator
{
    void Generate(in TextElementContext context, IList<InlineRun> output);
}

public enum TextAdornmentLayer
{
    Background,
    Text,
    Foreground
}

public interface ITextAdornment
{
    TextAdornmentLayer Layer { get; }
    void Draw(ITextRenderContext context, TextLineLayout line, Point origin);
}

public readonly record struct TextAdornmentContext(
    LogicalTextLine LogicalLine,
    ReadOnlyMemory<char> Text);

public interface ITextAdornmentProvider
{
    void GetAdornments(in TextAdornmentContext context, IList<ITextAdornment> output);
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

public sealed class TextViewExtensionPipeline
{
    public long Revision { get; set; }
    public IList<ITextClassifier> Classifiers { get; } = new List<ITextClassifier>();
    public IList<ITextLineTransformer> Transformers { get; } = new List<ITextLineTransformer>();
    public IList<ITextElementGenerator> ElementGenerators { get; } = new List<ITextElementGenerator>();
    public IList<ITextAdornmentProvider> AdornmentProviders { get; } = new List<ITextAdornmentProvider>();
    public IList<ITextProjection> Projections { get; } = new List<ITextProjection>();
}
