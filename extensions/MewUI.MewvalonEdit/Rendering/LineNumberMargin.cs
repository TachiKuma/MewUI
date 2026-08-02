using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

internal sealed class LineNumberMargin(TextEditor editor) : TextViewportMargin(editor)
{
    public Color NumberForeground { get; set; } = Color.FromRgb(128, 128, 128);

    protected override Size MeasureContent(Size availableSize)
    {
        int digits = Math.Max(2, Editor.Document.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        return new Size(digits * Math.Max(6, Editor.FontSize * 0.65) + 12, Math.Max(1, availableSize.Height));
    }

    protected override void OnRenderMargin(IGraphicsContext context)
    {
        context.FillRectangle(Bounds, Theme.Palette.ControlBackground);
    }

    protected override void OnRenderTextViewport(IGraphicsContext context, Rect textViewport)
    {
        var factory = GetGraphicsFactory();
        foreach (var line in Editor.Surface.VisibleTextLines)
        {
            string number = (line.LogicalLine.LineNumber + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var layout = factory.TextEngine.GetOrCreateLayout(
                new TextLayoutRequest
                {
                    Text = number.AsMemory(),
                    Dpi = GetDpi(),
                    DefaultStyle = new TextRunStyle(Editor.FontFamily, Editor.FontSize, Editor.FontWeight),
                    Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap }
                },
                TextLayoutCachePolicy.Content);
            double y = textViewport.Y + line.DocumentY - Editor.Surface.VerticalOffset;
            double x = Math.Max(Bounds.X + 4, Bounds.Right - layout.MeasuredSize.Width - 6);
            var options = new TextDrawOptions(NumberForeground);
            context.Text.Draw(layout, new Point(x, y), in options);
        }
    }
}
