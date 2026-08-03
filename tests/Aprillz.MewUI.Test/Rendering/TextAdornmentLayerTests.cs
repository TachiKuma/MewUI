using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextAdornmentLayerTests
{
    [TestMethod]
    public void AdornmentsDrawBetweenTheBackgroundAndGlyphPasses()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var order = new List<string>();
        var extensions = new TextViewExtensionPipeline();
        extensions.AdornmentProviders.Add(new RecordingAdornmentProvider(order));
        using var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            new StringTextDocument("hello"),
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(200, 50));
        var line = view.MaterializedLines[0];
        var recorder = new RecordingRenderContext(order);

        line.Draw(recorder, new Point(0, 0), new TextDrawOptions(Color.Black));
        line.DrawCaretLayer(recorder, new Point(0, 0));

        Assert.AreEqual(
            "Background,DrawBackground,Selection,DrawForeground,Text,Caret",
            string.Join(',', order));
    }

    private sealed class RecordingAdornmentProvider(List<string> order) : ITextAdornmentProvider
    {
        public void GetAdornments(in TextAdornmentContext context, IList<ITextAdornment> output)
        {
            foreach (var layer in Enum.GetValues<TextAdornmentLayer>())
            {
                output.Add(new RecordingAdornment(layer, order));
            }
        }
    }

    private sealed class RecordingAdornment(TextAdornmentLayer layer, List<string> order) : ITextAdornment
    {
        public TextAdornmentLayer Layer => layer;

        public void Draw(ITextRenderContext context, TextLineLayout line, Point origin) => order.Add(layer.ToString());
    }

    private sealed class RecordingRenderContext(List<string> order) : ITextRenderContext
    {
        public IGraphicsContext Graphics => throw new NotSupportedException();

        public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) => order.Add("Draw");

        public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options)
            => order.Add("DrawBackground");

        public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options)
            => order.Add("DrawForeground");
    }
}
