using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

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
/// A range of a visual line that a transformer can restyle. The engine builds the line itself, so
/// this carries the requested overrides rather than producing text runs.
/// </summary>
public class VisualLineElement
{
    internal VisualLineElement(int relativeTextOffset, int documentLength)
    {
        RelativeTextOffset = relativeTextOffset;
        DocumentLength = documentLength;
    }

    /// <summary>Offset from the start of the visual line.</summary>
    public int RelativeTextOffset { get; }

    public int DocumentLength { get; }

    /// <summary>Equal to <see cref="RelativeTextOffset"/>: this port maps visual columns onto line offsets.</summary>
    public int VisualColumn => RelativeTextOffset;

    public int VisualLength => DocumentLength;

    public VisualLineElementTextRunProperties TextRunProperties { get; } = new();

    /// <summary>Background painted behind the range. Equivalent to setting it through <see cref="TextRunProperties"/>.</summary>
    public Color? BackgroundBrush { get; set; }
}
