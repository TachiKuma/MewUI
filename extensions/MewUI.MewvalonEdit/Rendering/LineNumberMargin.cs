using System.Globalization;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

internal sealed class LineNumberMargin(TextEditor editor) : TextViewportMargin(editor)
{
    private const double LEFT_INSET = 4;
    private const double RIGHT_INSET = 6;
    private const int MIN_DIGITS = 2;

    private int _measuredDigits = -1;

    public Color NumberForeground { get; set; } = Color.FromRgb(128, 128, 128);

    /// <summary>Re-measures when the document grows or shrinks past a digit boundary.</summary>
    internal void SyncWidthToLineCount()
    {
        if (GetDigitCount() != _measuredDigits)
        {
            InvalidateMeasure();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        _measuredDigits = GetDigitCount();
        double width = MeasureNumberWidth(new string('9', _measuredDigits));
        return new Size(width + LEFT_INSET + RIGHT_INSET, Math.Max(1, availableSize.Height));
    }

    protected override void OnRenderTextViewport(IGraphicsContext context, Rect textViewport)
    {
        foreach (var line in Editor.Surface.VisibleTextLines)
        {
            string number = (line.LogicalLine.LineNumber + 1).ToString(CultureInfo.InvariantCulture);
            var layout = GetNumberLayout(number);
            double y = textViewport.Y + line.DocumentY - Editor.Surface.VerticalOffset;
            double x = Math.Max(Bounds.X + LEFT_INSET, Bounds.Right - layout.MeasuredSize.Width - RIGHT_INSET);
            var options = new TextDrawOptions(NumberForeground);
            context.Text.Draw(layout, new Point(x, y), in options);
        }
    }

    private int GetDigitCount()
        => Math.Max(MIN_DIGITS, Editor.Document.LineCount.ToString(CultureInfo.InvariantCulture).Length);

    // Measures the widest digits rather than estimating from font size, so proportional fonts
    // and high DPI do not clip the number.
    private double MeasureNumberWidth(string sample) => GetNumberLayout(sample).MeasuredSize.Width;

    private ITextLayout GetNumberLayout(string number)
        => GetGraphicsFactory().TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = number.AsMemory(),
                Dpi = GetDpi(),
                DefaultStyle = new TextRunStyle(Editor.FontFamily, Editor.FontSize, Editor.FontWeight),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap }
            },
            TextLayoutCachePolicy.Content);
}
