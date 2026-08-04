using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Controls;

/// <summary>
/// The selection is painted by a built-in view layer, so a change to the layer stack can stop it
/// drawing without any contract breaking. This renders for real and compares pixels.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SelectionRenderingTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 60;

    [TestMethod]
    public void SelectingTextChangesWhatIsPainted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        byte[] unselected = RenderMultiLine(selectionLength: 0);
        byte[] selected = RenderMultiLine(selectionLength: 6);

        Assert.IsGreaterThan(0, CountDifferingPixels(unselected, selected),
            "Selecting text painted nothing: the selection layer is not drawing.");
    }

    [TestMethod]
    public void SyntaxViewerSelectionChangesWhatIsPainted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        byte[] unselected = RenderSyntaxViewer(selectionLength: 0);
        byte[] selected = RenderSyntaxViewer(selectionLength: 6);

        Assert.IsGreaterThan(0, CountDifferingPixels(unselected, selected),
            "Selecting text painted nothing in SyntaxViewer.");
    }

    private static byte[] RenderMultiLine(int selectionLength)
    {
        var textBox = new MultiLineTextBox
        {
            Text = "select this line",
            Wrap = false,
            SkipViewportCull = true
        };
        textBox.Measure(new Size(WIDTH, HEIGHT));
        textBox.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        if (selectionLength > 0)
        {
            textBox.Select(0, selectionLength);
        }
        return Render(textBox);
    }

    private static byte[] RenderSyntaxViewer(int selectionLength)
    {
        var viewer = new SyntaxViewer
        {
            Text = "select this line",
            SkipViewportCull = true
        };
        viewer.Measure(new Size(WIDTH, HEIGHT));
        viewer.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        if (selectionLength > 0)
        {
            viewer.Select(0, selectionLength);
        }
        return Render(viewer);
    }

    private static byte[] Render(FrameworkElement element)
    {
        var factory = Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            element.Render(context);
            context.EndFrame();
        }
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }

    private static int CountDifferingPixels(byte[] left, byte[] right)
    {
        int differing = 0;
        for (int index = 0; index < Math.Min(left.Length, right.Length); index += 4)
        {
            if (left[index] != right[index] ||
                left[index + 1] != right[index + 1] ||
                left[index + 2] != right[index + 2])
            {
                differing++;
            }
        }
        return differing;
    }
}
