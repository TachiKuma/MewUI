extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class MewVGWin32TextPathTests
{
    [TestMethod]
    public void DrawTextLayout_RealizesGdiMeasuredTextIntoMewVGSurface()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        const int width = 240;
        const int height = 64;
        const string text = "office 한글";

        using var factory = new MewVGWin32GraphicsFactory();
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        using var context = factory.CreateContext(surface);
        using var font = factory.CreateFont("Segoe UI", 18, 96);

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.NoWrap,
            Trimming = TextTrimming.None
        };
        var constraints = new TextLayoutConstraints(new Rect(4, 4, width - 8, height - 8));
        var layout = context.CreateTextLayout(text, format, in constraints);

        Assert.IsNotNull(layout);
        Assert.IsGreaterThan(0, layout.MeasuredSize.Width);
        context.DrawTextLayout(text, format, layout, Color.White);
        context.EndFrame();

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int coveredPixels = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                coveredPixels++;
            }
        }

        Assert.IsGreaterThanOrEqualTo(5, coveredPixels,
            "GDI measurement succeeded, but the independent MewVG raster/image-pattern path produced no text pixels.");
    }
}
