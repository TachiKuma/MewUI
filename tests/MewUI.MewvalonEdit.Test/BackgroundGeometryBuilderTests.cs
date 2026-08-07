using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// A selection running over several lines is one shape, not a stack of boxes. The builder joins
/// rectangles whose edges meet into a single outline, which is what makes the corners round only on
/// the outside of the run.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class BackgroundGeometryBuilderTests
{
    [TestMethod]
    public void TouchingRectanglesBecomeOneFigure()
    {
        var builder = new BackgroundGeometryBuilder { CornerRadius = 3 };

        builder.AddRectangle(10, 0, 100, 20);
        builder.AddRectangle(10, 20, 60, 40);

        Assert.AreEqual(1, CountFigures(builder.CreateGeometry()),
            "Two rectangles that meet were drawn as separate outlines.");
    }

    [TestMethod]
    public void SeparatedRectanglesStayApart()
    {
        var builder = new BackgroundGeometryBuilder { CornerRadius = 3 };

        builder.AddRectangle(10, 0, 100, 20);
        builder.AddRectangle(10, 50, 60, 70);

        Assert.AreEqual(2, CountFigures(builder.CreateGeometry()),
            "Rectangles with a gap between them were joined.");
    }

    [TestMethod]
    public void AnEmptyBuilderHasNoGeometry()
        => Assert.IsNull(new BackgroundGeometryBuilder().CreateGeometry());

    /// <summary>
    /// The visual-column entry point, which is what a renderer holding a line rather than a
    /// document segment uses.
    /// </summary>
    [TestMethod]
    public void AVisualSegmentProducesOneRectanglePerRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = new TextEditor { Text = "class A\nint x;\n", ShowLineNumbers = false, SkipViewportCull = true };
        editor.Measure(new Size(360, 200));
        editor.Arrange(new Rect(0, 0, 360, 200));
        var line = editor.TextArea.TextView.VisualLines[0];

        var rects = BackgroundGeometryBuilder
            .GetRectsFromVisualSegment(editor.TextArea.TextView, line, 0, 5)
            .ToArray();

        Assert.HasCount(1, rects);
        Assert.IsGreaterThan(0.0, rects[0].Width, "The range covered no width.");
        Assert.AreEqual(line.TextLines[0].Bounds.Height, rects[0].Height, 0.01);
    }

    private static int CountFigures(PathGeometry? geometry)
    {
        if (geometry is null)
        {
            return 0;
        }
        int figures = 0;
        foreach (var command in geometry.Commands)
        {
            if (command.Type == PathCommandType.MoveTo)
            {
                figures++;
            }
        }
        return figures;
    }
}
