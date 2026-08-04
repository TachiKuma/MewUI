using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.Rendering;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The folding gutter's extent line and the outline around a collapsed placeholder are drawn, not
/// laid out, so only a real render shows them. Each case isolates the drawing under test: comparing
/// whole frames would pass on the box or the changed text alone.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FoldingVisualTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 160;
    private const int GUTTER_WIDTH = 20;
    private const string TEXT = "class A\n{\n    void M()\n    {\n        Body();\n    }\n}\n";

    // One brace pair only: a second folding would put another box in the band under test and the
    // comparison would pass on that box instead of on the extent line.
    private const string SINGLE_FOLD_TEXT = "class A\n{\n    int x;\n    int y;\n    int z;\n}\n";

    [TestMethod]
    public void AnExpandedSectionDrawsItsExtentBelowTheBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        byte[] withoutFoldings = Render(SINGLE_FOLD_TEXT, addFoldings: false, folded: false, outline: true);
        byte[] withFoldings = Render(SINGLE_FOLD_TEXT, addFoldings: true, folded: false, outline: true);

        // The document's only box sits on the second row, so anything differing further down the
        // gutter can only be the line running to the section end.
        double lineHeight = MeasureLineHeight(SINGLE_FOLD_TEXT);
        int bandTop = (int)Math.Ceiling(lineHeight * 3);
        int differing = CountDifferingPixels(
            withoutFoldings, withFoldings, new Rect(0, bandTop, GUTTER_WIDTH, HEIGHT - bandTop));

        Assert.IsGreaterThan(0, differing, "The gutter drew no extent line under the folding box.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MarginsStayLeftOfTheTextWhenOneIsAdded(bool showLineNumbers)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = new TextEditor
        {
            Text = TEXT,
            ShowLineNumbers = showLineNumbers,
            SkipViewportCull = true
        };
        // Adding a margin rebuilds the host grid, which transfers the surface to the new grid; a
        // grid position assigned before that transfer is lost and the margin lands to the right.
        FoldingManager.Install(editor);
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        var margin = editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single();
        Assert.IsLessThan(editor.TextArea.TextView.Host.TextViewportBounds.X, margin.Bounds.X,
            "The folding margin must sit left of the text.");
    }

    private static double MeasureLineHeight(string text)
    {
        var editor = new TextEditor { Text = text, ShowLineNumbers = false, SkipViewportCull = true };
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        return editor.TextArea.TextView.DefaultLineHeight;
    }

    [TestMethod]
    public void CollapsedPlaceholderIsOutlined()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // Same collapsed document either way, so the whole frame already isolates the renderer;
        // no region is needed and the placeholder sits close to the gutter.
        byte[] withoutOutline = Render(TEXT, addFoldings: true, folded: true, outline: false);
        byte[] withOutline = Render(TEXT, addFoldings: true, folded: true, outline: true);

        int differing = CountDifferingPixels(withoutOutline, withOutline, new Rect(0, 0, WIDTH, HEIGHT));

        Assert.IsGreaterThan(0, differing, "The collapsed placeholder was not outlined.");
    }

    private static byte[] Render(string text, bool addFoldings, bool folded, bool outline)
    {
        var editor = new TextEditor
        {
            Text = text,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        var manager = FoldingManager.Install(editor);
        if (!outline)
        {
            // The manager registers exactly one background renderer, the placeholder outline.
            editor.TextArea.TextView.BackgroundRenderers.Clear();
        }
        if (addFoldings)
        {
            new BraceFoldingStrategy().UpdateFoldings(manager, editor.Document);
            if (folded)
            {
                var first = manager.AllFoldings.FirstOrDefault();
                Assert.IsNotNull(first, "The brace strategy found no folding to collapse.");
                first.IsFolded = true;
            }
        }
        editor.Measure(new Size(WIDTH, HEIGHT));
        editor.Arrange(new Rect(0, 0, WIDTH, HEIGHT));

        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            editor.Render(context);
            context.EndFrame();
        }
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }

    private static int CountDifferingPixels(byte[] left, byte[] right, Rect region)
    {
        int differing = 0;
        for (int y = (int)region.Y; y < (int)region.Bottom && y < HEIGHT; y++)
        {
            for (int x = (int)region.X; x < (int)region.Right && x < WIDTH; x++)
            {
                int offset = (y * WIDTH + x) * 4;
                if (offset + 2 >= Math.Min(left.Length, right.Length))
                {
                    continue;
                }
                if (left[offset] != right[offset] ||
                    left[offset + 1] != right[offset + 1] ||
                    left[offset + 2] != right[offset + 2])
                {
                    differing++;
                }
            }
        }
        return differing;
    }
}
