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
    public void EachAdornmentDrawsUnderItsOwnLayerContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var order = new List<string>();
        var (line, recorder) = CreateLine(order);

        line.Draw(recorder, new Point(0, 0), new TextDrawOptions(Color.Black));

        // Background adornments then the backgrounds, selection adornments then (a host would paint
        // the selection here), text adornments then the glyphs, caret adornments then the caret.
        Assert.AreEqual(
            "Background,DrawBackground,Selection,Text,DrawForeground,Caret",
            string.Join(',', order));
    }

    [TestMethod]
    public void HostsInterleaveViewportRenderersAtTheSameAnchors()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var order = new List<string>();
        var (line, recorder) = CreateLine(order);
        var options = new TextDrawOptions(Color.Black);
        var origin = new Point(0, 0);

        foreach (var layer in Enum.GetValues<TextAdornmentLayer>())
        {
            order.Add($"Viewport{layer}");
            line.DrawAdornmentLayer(recorder, origin, layer);
            switch (layer)
            {
                case TextAdornmentLayer.Background:
                    line.DrawBackground(recorder, origin, in options);
                    break;
                case TextAdornmentLayer.Selection:
                    order.Add("SelectionHighlight");
                    break;
                case TextAdornmentLayer.Text:
                    line.DrawForeground(recorder, origin, in options);
                    break;
                default:
                    order.Add("Caret");
                    break;
            }
        }

        Assert.AreEqual(
            "ViewportBackground,Background,DrawBackground," +
            "ViewportSelection,Selection,SelectionHighlight," +
            "ViewportText,Text,DrawForeground," +
            "ViewportCaret,Caret,Caret",
            string.Join(',', order));
    }

    private static (TextLineLayout Line, RecordingRenderContext Recorder) CreateLine(List<string> order)
    {
        var extensions = new TextViewExtensionPipeline();
        extensions.AdornmentProviders.Add(new RecordingAdornmentProvider(order));
        var factory = new GdiGraphicsFactory();
        var view = new TextViewLayout(
            factory.TextEngine,
            new StringTextDocument("hello"),
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            extensions,
            dpi: 96);
        view.SetViewport(new TextViewport(200, 50));
        return (view.MaterializedLines[0], new RecordingRenderContext(order));
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
