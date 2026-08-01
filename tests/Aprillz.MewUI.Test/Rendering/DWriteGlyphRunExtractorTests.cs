using Aprillz.MewUI;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class DWriteGlyphRunExtractorTests
{
    [TestMethod]
    public void Capture_CopiesGlyphAndClusterDataFromTextLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        const string text = "office 한글 😀";
        using var factory = new Direct2DGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(400, 80, 1));
        using var context = factory.CreateContext(surface);
        using var font = factory.CreateFont("Segoe UI", 16, 96);

        context.BeginFrame(surface);
        try
        {
            var format = new TextFormat
            {
                Font = font,
                HorizontalAlignment = TextAlignment.Left,
                VerticalAlignment = TextAlignment.Top,
                Wrapping = TextWrapping.NoWrap,
                Trimming = TextTrimming.None
            };
            var constraints = new TextLayoutConstraints(new Rect(0, 0, 400, 80));
            var layout = context.CreateTextLayout(text, format, in constraints);

            Assert.IsNotNull(layout);
            Assert.AreNotEqual(0, layout.BackendHandle);

            var runs = DWriteGlyphRunExtractor.Capture(layout.BackendHandle);

            Assert.IsNotEmpty(runs);
            Assert.AreEqual(text.Length, runs.Sum(run => checked((int)run.TextLength)));
            Assert.IsGreaterThanOrEqualTo(2, runs.Select(run => run.FaceIndex).Distinct().Count(),
                "The mixed Latin/Hangul/emoji sample should preserve fallback face boundaries.");
            foreach (var run in runs)
            {
                Assert.IsGreaterThan(0, run.GlyphIndices.Length);
                Assert.HasCount(run.GlyphIndices.Length, run.Advances);
                Assert.HasCount(run.GlyphIndices.Length, run.Offsets);
                Assert.HasCount(checked((int)run.TextLength), run.ClusterMap);
                Assert.IsGreaterThan(0, run.Advances.Sum());
            }
        }
        finally
        {
            context.EndFrame();
        }
    }
}
