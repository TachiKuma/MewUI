using Aprillz.MewUI.Input;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Cursor query an element answers while the pointer is over it.</summary>
public sealed class QueryCursorEventArgs(Point position, ModifierKeys modifiers)
{
    public Point Position { get; } = position;
    public ModifierKeys Modifiers { get; } = modifiers;

    /// <summary>Cursor to show. Leave null to keep the editor's own text cursor.</summary>
    public CursorType? Cursor { get; set; }
}

/// <summary>Font selection of a text run. Replaces WPF's Typeface.</summary>
public sealed record Typeface(string FontFamily, FontWeight Weight = FontWeight.Normal, bool Italic = false);

/// <summary>
/// Paint and font overrides a transformer applies to a range. Mirrors AvalonEdit's type of the same
/// name; brush parameters are <see cref="Color"/> following the MewUI convention.
/// </summary>
public sealed class VisualLineElementTextRunProperties
{
    public Color? ForegroundBrush { get; private set; }
    public Color? BackgroundBrush { get; private set; }
    public string? FontFamily { get; private set; }
    public double? FontRenderingEmSize { get; private set; }
    public FontWeight? FontWeight { get; private set; }
    public bool? Italic { get; private set; }
    public TextDecoration TextDecorations { get; private set; }

    public void SetForegroundBrush(Color value) => ForegroundBrush = value;

    public void SetBackgroundBrush(Color value) => BackgroundBrush = value;

    public void SetFontRenderingEmSize(double value) => FontRenderingEmSize = value;

    public void SetTextDecorations(TextDecoration value) => TextDecorations = value;

    public void SetTypeface(Typeface value)
    {
        ArgumentNullException.ThrowIfNull(value);
        FontFamily = value.FontFamily;
        FontWeight = value.Weight;
        Italic = value.Italic;
    }

    internal bool HasPaint => ForegroundBrush.HasValue || BackgroundBrush.HasValue || TextDecorations != TextDecoration.None;

    internal bool HasFont => FontFamily is not null || FontRenderingEmSize.HasValue || FontWeight.HasValue || Italic.HasValue;
}

/// <summary>
/// A range of a visual line. Transformers restyle one through <see cref="TextRunProperties"/>;
/// element generators produce one that measures and draws itself in place of the document text.
/// </summary>
public class VisualLineElement
{
    protected VisualLineElement(int visualLength, int documentLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(visualLength, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(documentLength);
        VisualLength = visualLength;
        DocumentLength = documentLength;
    }

    public int VisualLength { get; }

    public int DocumentLength { get; }

    /// <summary>Offset from the start of the visual line. Assigned while the line is built.</summary>
    public int RelativeTextOffset { get; internal set; }

    /// <summary>Equal to <see cref="RelativeTextOffset"/>: this port maps visual columns onto line offsets.</summary>
    public int VisualColumn => RelativeTextOffset;

    public VisualLineElementTextRunProperties TextRunProperties { get; } = new();

    /// <summary>Background painted behind the range. Equivalent to setting it through <see cref="TextRunProperties"/>.</summary>
    public Color? BackgroundBrush { get; set; }

    /// <summary>Size this element occupies. Generated elements override it; a restyled range keeps the document text.</summary>
    public virtual InlineMetrics Measure() => default;

    /// <summary>Paints this element. Generated elements override it.</summary>
    public virtual void Draw(ITextRenderContext context, Point origin)
    {
    }

    /// <summary>
    /// Text this element occupies on the visual surface. Differs from the document text only when
    /// <see cref="VisualLength"/> and <see cref="DocumentLength"/> differ; the default fills with
    /// object replacement characters since the element paints over them anyway.
    /// </summary>
    protected internal virtual string GetVisualText() => new('￼', VisualLength);

    /// <summary>
    /// Called when the pointer is pressed over this element, before the editor moves the caret.
    /// Setting <see cref="MouseEventArgs.Handled"/> claims the press and skips caret placement.
    /// </summary>
    protected internal virtual void OnMouseDown(MouseEventArgs e)
    {
    }

    /// <summary>Called while the pointer is over this element to pick the cursor.</summary>
    protected internal virtual void OnQueryCursor(QueryCursorEventArgs e)
    {
    }
}

/// <summary>Range whose only purpose is to carry a transformer's overrides.</summary>
internal sealed class StyleOverrideElement : VisualLineElement
{
    public StyleOverrideElement(int relativeTextOffset, int length) : base(Math.Max(1, length), length)
        => RelativeTextOffset = relativeTextOffset;
}
