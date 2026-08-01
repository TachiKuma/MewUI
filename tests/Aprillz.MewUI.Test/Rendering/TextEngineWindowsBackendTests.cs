extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Native.DirectWrite;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextEngineWindowsBackendTests
{
    [TestMethod]
    public void Direct2D_FullPathMeasureHitAndDrawAreSelfConsistent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertBackend(factory);
        AssertTenMegabyteFastPath(factory);
        AssertEditorBackend(factory);
    }

    [TestMethod]
    public void Gdi_FullPathMeasureHitAndDrawAreSelfConsistent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertBackend(factory);
        AssertTenMegabyteFastPath(factory);
        AssertEditorBackend(factory);
    }

    [TestMethod]
    public void MewVGWin32_FullPathMeasureHitAndDrawAreSelfConsistent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var scope = factory.AcquireBackgroundRenderScope();
        AssertBackend(factory);
        AssertTenMegabyteFastPath(factory);
        AssertEditorBackend(factory);
    }

    private static void AssertBackend(IGraphicsFactory factory)
    {
        const string text = "office 한글 😀";
        const int width = 320;
        const int height = 72;
        var request = new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 18),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = width - 8,
                Wrapping = TextWrapping.Wrap
            }
        };
        var layout = factory.TextEngine.CreateLayout(request);

        Assert.HasCount(1, layout.Lines);
        var endCaret = layout.GetCaretBounds(new CharacterHit(text.Length, 0));
        Assert.AreEqual(layout.MeasuredSize.Width, endCaret.X, 1.5,
            $"{factory.Backend}: end caret and measured width diverged.");
        var endHit = layout.HitTestPoint(new Point(endCaret.X, endCaret.Y + endCaret.Height * 0.5));
        Assert.AreEqual(text.Length, endHit.InsertionIndex);

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        DWriteGlyphRunExtractor.GlyphRun? nativeRealization = null;
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.Transparent);
            context.Text.Draw(layout, new Point(4, 4), new TextDrawOptions(Color.White));
            context.EndFrame();
            if (factory is Direct2DGraphicsFactory)
            {
                nativeRealization = ((Direct2DTextRenderContext)context.Text).CachedGlyphRuns
                    .First(static item => item.HasOwnedFontFace);
            }
        }

        if (nativeRealization is not null)
        {
            Assert.IsFalse(nativeRealization.HasOwnedFontFace,
                "Disposing the graphics context did not release its DirectWrite font face realization.");
        }

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int covered = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                covered++;
            }
        }
        Assert.IsGreaterThanOrEqualTo(5, covered, $"{factory.Backend}: new text draw surface produced no ink.");
    }

    private static void AssertEditorBackend(IGraphicsFactory factory)
    {
        const int width = 260;
        const int height = 120;
        var previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var editor = new NewMultiLineTextBox
            {
                Width = width,
                Height = height,
                Text = "first office 한글 😀\n" + new string('W', 80),
                Wrap = true
            };
            using var window = HeadlessWindow.Create(width, height);
            window.Content = editor;
            window.PerformLayout();
            editor.Focus();
            editor.CaretPosition = 1;
            Rect before = editor.GetCharRectInWindow(editor.CaretPosition);
            window.SendKeyPress(Key.Down);
            Rect after = editor.GetCharRectInWindow(editor.CaretPosition);

            Assert.IsGreaterThan(1, editor.CaretPosition, $"{factory.Backend}: editor caret did not move.");
            Assert.IsGreaterThan(before.Y, after.Y, $"{factory.Backend}: editor did not move by a visual row.");

            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
            window.RenderFrameToSurface(surface);
            var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
            int covered = 0;
            for (int index = 3; index < pixels.Length; index += 4)
            {
                if (pixels[index] > 16) covered++;
            }
            Assert.IsGreaterThan(5, covered, $"{factory.Backend}: NewMultiLineTextBox produced no ink.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static void AssertTenMegabyteFastPath(IGraphicsFactory factory)
    {
        const int width = 320;
        const int height = 48;
        string text = new('x', 10_000_000);
        var owner = new object();
        var layout = (ManagedTextLayout)factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle("Segoe UI", 16),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
                Revision = 1
            },
            TextLayoutCachePolicy.Owner,
            owner);

        Assert.IsTrue(layout.IsFastPath);
        Assert.IsGreaterThan(width, layout.MeasuredSize.Width,
            $"{factory.Backend}: 10MB line width was not measured.");
        Rect endCaret = layout.GetCaretBounds(new CharacterHit(text.Length, 0));
        Assert.AreEqual(layout.MeasuredSize.Width, endCaret.X, 0.01);

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.Text.Draw(layout, Point.Zero, new TextDrawOptions(Color.White, Owner: owner));
        context.EndFrame();

        Assert.IsFalse(layout.HasMaterializedClusters,
            $"{factory.Backend}: 10MB fast draw materialized per-grapheme objects.");
        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int covered = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 16) covered++;
        }
        Assert.IsGreaterThan(5, covered, $"{factory.Backend}: 10MB fast path produced no ink.");
    }
}
