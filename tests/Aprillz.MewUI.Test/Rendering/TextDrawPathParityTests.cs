using System;
using System.Collections.Generic;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

/// <summary>
/// A centred DrawText and a centred DrawTextLayout must put ink on the same pixel rows: controls
/// swap between the two (an editor replacing its label, a placeholder replacing a document), so any
/// divergence shows up as text jumping. Compares first/last ink rows per backend across DPI scales
/// and box heights.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextDrawPathParityTests
{
    private const string TEXT = "Change source.Value";
    private const int WIDTH_PX = 320;
    private const int HEIGHT_PX = 64;

    [TestMethod]
    public void Direct2D_DrawText_MatchesDrawTextLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
        AssertParity(factory);
    }

    [TestMethod]
    public void Gdi_DrawText_MatchesDrawTextLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();
        AssertParity(factory);
    }

    private static void AssertParity(IGraphicsFactory factory)
    {
        var mismatches = new List<string>();

        foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            for (int hStep = 0; hStep < 6; hStep++)
            {
                double boxHeight = 20 + hStep;
                var viaLayout = InkRows(factory, scale, boxHeight, useLayout: true);
                var viaDrawText = InkRows(factory, scale, boxHeight, useLayout: false);
                if (viaLayout != viaDrawText)
                {
                    mismatches.Add(
                        $"scale={scale:0.##} boxH={boxHeight}: layout[{viaLayout.Top}..{viaLayout.Bottom}] " +
                        $"drawText[{viaDrawText.Top}..{viaDrawText.Bottom}]");
                }
            }
        }

        Assert.IsEmpty(mismatches,
            $"DrawText ink diverges from DrawTextLayout in {mismatches.Count} of 30 cases: {string.Join("; ", mismatches)}");
    }

    private static (int Top, int Bottom) InkRows(IGraphicsFactory factory, double scale, double boxHeightDip, bool useLayout)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH_PX, HEIGHT_PX, scale));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.FromArgb(255, 255, 255, 255));

            using var font = factory.CreateFont("Segoe UI", 12, (uint)Math.Round(scale * 96));
            var format = new TextFormat
            {
                Font = font,
                HorizontalAlignment = TextAlignment.Left,
                VerticalAlignment = TextAlignment.Center,
                Wrapping = TextWrapping.NoWrap,
                Trimming = TextTrimming.None,
            };

            var box = new Rect(2, 2, 240, boxHeightDip);
            if (useLayout)
            {
                var layout = context.CreateTextLayout(TEXT, format, new TextLayoutConstraints(box));
                if (layout != null)
                {
                    layout.EffectiveBounds = box;
                    context.DrawTextLayout(TEXT, format, layout, Color.FromArgb(255, 0, 0, 0));
                }
            }
            else
            {
                context.DrawText(TEXT, box, font, Color.FromArgb(255, 0, 0, 0),
                    TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
            }

            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;

        int top = -1;
        int bottom = -1;
        for (int y = 0; y < HEIGHT_PX; y++)
        {
            for (int x = 0; x < WIDTH_PX; x++)
            {
                if (pixels[y * stride + x * 4 + 1] < 200)
                {
                    if (top < 0)
                    {
                        top = y;
                    }

                    bottom = y;
                    break;
                }
            }
        }

        return (top, bottom);
    }
}
