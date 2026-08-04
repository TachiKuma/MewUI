using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// A stationary viewport must keep showing the same text. The long-line virtualizer maps a scroll
/// offset to a character offset, so anything that moves that mapping moves the content under the
/// reader, which is what refining a width estimate used to do.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LongLineScrollStabilityTests
{
    private const int LINE_LENGTH = 400_000;

    [TestMethod]
    public void RepeatedLayoutAtTheSameOffsetShowsTheSameText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var view = CreateView("Segoe UI", out _);
        var viewport = new TextViewport(600, 200, HorizontalOffset: 120_000);

        view.SetViewport(viewport);
        string first = DescribeFirstLine(view);

        for (int pass = 0; pass < 6; pass++)
        {
            view.SetViewport(viewport with { HorizontalOffset = viewport.HorizontalOffset + 1 });
            view.SetViewport(viewport);
        }

        Assert.AreEqual(first, DescribeFirstLine(view),
            "The visible slice drifted while the scroll offset stayed put.");
    }

    [TestMethod]
    public void ScrollingBackReturnsToTheSameText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var view = CreateView("Segoe UI", out _);
        var start = new TextViewport(600, 200, HorizontalOffset: 50_000);

        view.SetViewport(start);
        string before = DescribeFirstLine(view);

        foreach (double offset in new double[] { 200_000, 900_000, 400_000, 90_000 })
        {
            view.SetViewport(start with { HorizontalOffset = offset });
        }
        view.SetViewport(start);

        Assert.AreEqual(before, DescribeFirstLine(view),
            "Returning to the same offset landed on different text.");
    }

    private static string DescribeFirstLine(TextViewLayout view)
    {
        var line = view.MaterializedLines[0];
        return $"{line.LogicalLine.Offset}+{line.LogicalLine.Length}@{line.DocumentX:F2}";
    }

    private static TextViewLayout CreateView(string fontFamily, out IReadOnlyTextDocument document)
    {
        // Deliberately uneven advances: the estimate a slice observes then depends on which slice
        // was measured, which is what makes the mapping move.
        const string PATTERN = "iiiiWWMMlliii W";
        var text = string.Create(LINE_LENGTH, 0, static (span, _) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                span[index] = PATTERN[index % PATTERN.Length];
            }
        });
        var source = new StringTextDocument(text);
        document = source;
        var factory = new GdiGraphicsFactory();
        var view = new TextViewLayout(
            factory.TextEngine,
            source,
            new TextRunStyle(fontFamily, 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            new TextViewExtensionPipeline(),
            dpi: 96);
        return view;
    }
}
