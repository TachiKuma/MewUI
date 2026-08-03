using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Replaces ranges of the document with elements that draw themselves. The scan protocol matches
/// AvalonEdit: the builder asks each generator where it wants to act, then asks the winner to build.
/// </summary>
public abstract class VisualLineElementGenerator
{
    protected ITextRunConstructionContext? CurrentContext { get; private set; }

    public virtual void StartGeneration(ITextRunConstructionContext context)
        => CurrentContext = context ?? throw new ArgumentNullException(nameof(context));

    public virtual void FinishGeneration() => CurrentContext = null;

    /// <summary>First offset at or after <paramref name="startOffset"/> this generator wants, or -1.</summary>
    public abstract int GetFirstInterestedOffset(int startOffset);

    /// <summary>Builds the element at <paramref name="offset"/>, or null to decline.</summary>
    public abstract VisualLineElement? ConstructElement(int offset);
}

/// <summary>Draws replacement text in place of the document range it covers.</summary>
public class TextReplacementElement : VisualLineElement
{
    private readonly TextRunStyle _style;

    /// <param name="style">Resolved when the element is built; generation context is gone by the time it draws.</param>
    public TextReplacementElement(string text, int documentLength, TextRunStyle style)
        : base(Math.Max(1, text?.Length ?? 0), documentLength)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _style = style;
    }

    public string Text { get; }

    /// <summary>Color of the replacement text. Falls back to the document foreground when unset.</summary>
    public Color? Foreground { get; set; }

    public override InlineMetrics Measure()
    {
        var layout = CreateLayout();
        return new InlineMetrics(layout.MeasuredSize.Width, layout.MeasuredSize.Height, layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new TextDrawOptions(Foreground ?? TextRunProperties.ForegroundBrush ?? Color.FromRgb(0, 0, 0));
        context.Draw(CreateLayout(), origin, in options);
    }

    private ITextLayout CreateLayout()
    {
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        return factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = Text.AsMemory(),
                DefaultStyle = _style,
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap, MaxWidth = double.PositiveInfinity }
            },
            TextLayoutCachePolicy.Content);
    }
}
