using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// Advancing the highlighting state up to the viewport raises one notification per scanned line.
/// The colorizer suppresses those for the ongoing pass - without the guard, a jump to the end of a
/// large document issues one full viewport rebuild per line (20,000 rebuilds for 20,000 lines).
/// </summary>
[TestClass]
public sealed class HighlightingStateScanTests
{
    [TestMethod]
    public void TheStateScanToTheViewportDoesNotRepaintPerLine()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var document = new TextDocument(
            string.Join("\n", Enumerable.Range(1, 500).Select(n => $"public static int Value{n} => {n}; // line {n}")));
        var colorizer = new HighlightingColorizer(definition);
        int repaints = 0;
        colorizer.HighlightingStateChanged += (_, _) => repaints++;

        colorizer.OnVisualLineConstructionStarting(document, 500);
        colorizer.OnVisualLinesChanged();

        // Only a change crossing the first line in view may repaint; the scan itself must not.
        Assert.IsTrue(repaints <= 1, $"The scan to the viewport issued {repaints} repaints.");
    }

    [TestMethod]
    public void AStateChangeBelowTheColorizedLineStillRepaints()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var document = new TextDocument("int before;\nint after;");
        var colorizer = new HighlightingColorizer(definition);
        int repaints = 0;
        colorizer.HighlightingStateChanged += (_, _) => repaints++;

        // Scanning to line 0 keeps the whole document ahead of the pass, so opening a comment on
        // line 1 must still reach the host: the state at line 2 changed below the scanned range.
        colorizer.OnVisualLineConstructionStarting(document, 1);
        colorizer.OnVisualLinesChanged();
        int baseline = repaints;
        document.Insert(0, "/*");
        colorizer.GetHighlighter(document).HighlightLine(1);

        Assert.IsTrue(repaints > baseline,
            "A state change crossing below the highlighted line was suppressed.");
    }
}
