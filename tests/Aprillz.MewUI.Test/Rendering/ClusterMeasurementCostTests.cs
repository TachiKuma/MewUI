using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class ClusterMeasurementCostTests
{
    [TestMethod]
    public void WrappedTextMeasuresPerRunNotPerCluster()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var inner = new GdiGraphicsFactory();
        using var factory = new CountingGraphicsFactory(inner);
        Application.DefaultGraphicsFactory = factory;
        try
        {
            // Wrapping keeps this off the fast path, so it goes through cluster measurement.
            string text = new string('a', 200);
            factory.MeasureTextCalls = 0;
            ((IGraphicsFactory)factory).TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle("Consolas", 12),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.Wrap, MaxWidth = 100 }
            });

            Assert.IsLessThan(
                10,
                factory.MeasureTextCalls,
                $"Cluster measurement should stay per-run; {factory.MeasureTextCalls} calls for {text.Length} characters.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    // Delegates everything except measurement, which it counts.
    private sealed class CountingGraphicsFactory(IGraphicsFactory inner) : IGraphicsFactory
    {
        public int MeasureTextCalls;

        public string Backend => inner.Backend;

        public IGraphicsContext CreateMeasurementContext(uint dpi)
            => new CountingContext(inner.CreateMeasurementContext(dpi), this);

        public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
            bool italic = false, bool underline = false, bool strikethrough = false)
            => inner.CreateFont(family, size, weight, italic, underline, strikethrough);

        public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
            bool italic = false, bool underline = false, bool strikethrough = false)
            => inner.CreateFont(family, size, dpi, weight, italic, underline, strikethrough);

        public IImage CreateImageFromFile(string path) => inner.CreateImageFromFile(path);
        public IImage CreateImageFromBytes(byte[] data) => inner.CreateImageFromBytes(data);
        public IGraphicsContext CreateContext(IRenderTarget target) => inner.CreateContext(target);
        public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor) => inner.CreateSurface(descriptor);
        public IGraphicsContext CreateContext(IRenderSurface surface) => inner.CreateContext(surface);
        public IImage CreateImageView(IRenderSurface surface) => inner.CreateImageView(surface);
        public IImage CreateImageView(IPixelBufferSource source) => inner.CreateImageView(source);
        public IImage CreateImageView(IExternalRasterSource source) => inner.CreateImageView(source);

        public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
            => inner.TryReadPixels(source, destination, destinationStrideBytes);

        public IRenderOperation RequestReadback(IRenderSurface source) => inner.RequestReadback(source);
        public IRenderOperation FlushAsyncWork() => inner.FlushAsyncWork();
        public IRenderResourceCache? ResourceCache => inner.ResourceCache;
        public IRenderEffectDevice? Effects => inner.Effects;

        public void Dispose() { }
    }

    // Forwards ITextAdvanceSource so the engine takes the same path a real backend context does.
    private sealed class CountingContext(IGraphicsContext inner, CountingGraphicsFactory owner)
        : MeasureGraphicsContextBase, ITextAdvanceSource
    {
        double[] ITextAdvanceSource.GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font)
            => ((ITextAdvanceSource)inner).GetUtf16PrefixAdvances(text, font);

        public override double DpiScale => inner.DpiScale;

        public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        {
            owner.MeasureTextCalls++;
            return inner.MeasureText(text, font);
        }

        public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        {
            owner.MeasureTextCalls++;
            return inner.MeasureText(text, font, maxWidth);
        }

        public override TextLayout? CreateTextLayout(ReadOnlySpan<char> text, TextFormat format,
            in TextLayoutConstraints constraints)
            => inner.CreateTextLayout(text, format, in constraints);

        public override void Dispose() => inner.Dispose();
    }
}
