using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.CodeCompletion;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Search;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Platform;
using System.Diagnostics;

namespace Aprillz.MewUI.MewvalonEdit.Test;

[TestClass]
[DoNotParallelize]
public sealed class EditorExtensionTests
{
    [TestMethod]
    public void SearchFindsSelectsAndReplacesMatches()
    {
        var editor = new TextEditor { Text = "Alpha beta alpha" };
        var search = SearchPanel.Install(editor);
        search.SearchPattern = "alpha";

        Assert.HasCount(2, search.Results);
        Assert.AreEqual(0, search.FindNext(0)?.Offset);
        Assert.AreEqual(5, editor.SelectionLength);
        Assert.AreEqual(2, search.ReplaceAll("item"));
        Assert.AreEqual("item beta item", editor.Text);

        editor.Document = new TextDocument("alpha replacement");
        Assert.HasCount(1, search.Results);

        search.Uninstall();
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void PlainSearchUpdatesOnlyTheChangedNeighborhood()
    {
        string text = "needle " + new string('x', 2_000_000) + " needle";
        var editor = new TextEditor { Text = text };
        var search = SearchPanel.Install(editor);
        search.SearchPattern = "needle";
        Assert.HasCount(2, search.Results);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        editor.Document.Insert(100, "x");

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.IsLessThan(100L, stopwatch.ElapsedMilliseconds,
            $"Incremental search refresh took {stopwatch.ElapsedMilliseconds}ms.");
        Assert.IsLessThan(1L * 1024 * 1024, allocated,
            $"Incremental search allocated {allocated:N0} bytes, indicating a whole-document rescan.");
        Assert.HasCount(2, search.Results);
        Assert.AreEqual(text.LastIndexOf("needle", StringComparison.Ordinal) + 1, search.Results[1].Offset);
        search.Uninstall();
    }

    [TestMethod]
    public void DefaultIndentationCopiesPreviousLineWhitespace()
    {
        var document = new TextDocument("\t  parent\nchild");
        var strategy = new DefaultIndentationStrategy();

        strategy.IndentLine(document, document.GetLineByNumber(2));

        Assert.AreEqual("\t  parent\n\t  child", document.Text);
    }

    /// <summary>
    /// The strategy has to be reached by pressing Enter, not only by calling it. Storing it without
    /// a caller leaves an editor that quietly refuses to indent.
    /// </summary>
    [TestMethod]
    public void PressingEnterRunsTheIndentationStrategy()
    {
        var editor = new TextEditor
        {
            Text = "\t  parent",
            IndentationStrategy = new DefaultIndentationStrategy()
        };
        editor.CaretOffset = editor.Document.TextLength;

        editor.TextArea.PerformTextInput("\n");

        Assert.AreEqual("\t  parent\n\t  ", editor.Text);
        Assert.AreEqual(editor.Document.TextLength, editor.CaretOffset,
            "The caret ends after the indentation it was given.");
    }

    /// <summary>
    /// A converted tab fills to the next stop, not a whole indent. Always inserting IndentationSize
    /// spaces overshoots every stop but the first, so columns stop lining up.
    /// </summary>
    [TestMethod]
    public void ConvertedTabsStopAtTheNextIndentationStop()
    {
        var editor = new TextEditor { Text = "ab" };
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.CaretOffset = 2;

        editor.TextArea.PerformTextInput("\t");

        Assert.AreEqual("ab  ", editor.Text, "Column 3 reaches the stop at 5 with two spaces.");

        editor.TextArea.PerformTextInput("\t");
        Assert.AreEqual("ab      ", editor.Text, "The next tab takes a full indent from the stop.");
    }

    /// <summary>
    /// With nothing selected, copy takes the whole line including its terminator, which is what the
    /// original does and why the option defaults on.
    /// </summary>
    [TestMethod]
    public void CopyWithoutASelectionTakesTheWholeLine()
    {
        var clipboard = new RecordingClipboard();
        var editor = new TextEditor { Text = "first\nsecond\nthird" };
        editor.Surface.ClipboardService = clipboard;
        editor.CaretOffset = 8;

        editor.Copy();

        Assert.AreEqual("second\n", clipboard.Text);
        Assert.AreEqual(8, editor.CaretOffset, "Copying leaves the caret where it was.");
        Assert.AreEqual(0, editor.SelectionLength);
    }

    [TestMethod]
    public void CutWithoutASelectionRemovesTheWholeLine()
    {
        var clipboard = new RecordingClipboard();
        var editor = new TextEditor { Text = "first\nsecond\nthird" };
        editor.Surface.ClipboardService = clipboard;
        editor.CaretOffset = 8;

        editor.Cut();

        Assert.AreEqual("second\n", clipboard.Text);
        Assert.AreEqual("first\nthird", editor.Text);
    }

    [TestMethod]
    public void TurningTheWholeLineOptionOffLeavesCopyAlone()
    {
        var clipboard = new RecordingClipboard();
        var editor = new TextEditor { Text = "first\nsecond\nthird" };
        editor.Surface.ClipboardService = clipboard;
        editor.Options.CutCopyWholeLine = false;
        editor.CaretOffset = 8;

        editor.Copy();

        Assert.AreEqual(string.Empty, clipboard.Text, "Nothing is selected, so nothing is copied.");
    }

    /// <summary>
    /// Links come with the editor rather than having to be registered, which is what the two options
    /// defaulting on mean. ILSpy sets RequireControlModifierForHyperlinkClick, so it has to arrive
    /// at the generator.
    /// </summary>
    [TestMethod]
    public void LinkGeneratorsFollowTheirOptions()
    {
        var editor = new TextEditor { Text = "see www.example.com and a@b.com" };
        var view = editor.TextArea.TextView;

        Assert.AreEqual(1, view.ElementGenerators.OfType<LinkElementGenerator>().Count(
            static generator => generator is not MailLinkElementGenerator));
        Assert.AreEqual(1, view.ElementGenerators.OfType<MailLinkElementGenerator>().Count());
        Assert.IsTrue(view.ElementGenerators.OfType<LinkElementGenerator>()
            .All(static generator => generator.RequireControlModifierForClick));

        editor.Options.RequireControlModifierForHyperlinkClick = false;
        Assert.IsTrue(view.ElementGenerators.OfType<LinkElementGenerator>()
            .All(static generator => !generator.RequireControlModifierForClick));

        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        Assert.IsEmpty(view.ElementGenerators.OfType<LinkElementGenerator>());
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }

        public bool TryGetText(out string text)
        {
            text = Text;
            return true;
        }
    }

    /// <summary>Ordinary typing must not re-run the strategy over the line.</summary>
    [TestMethod]
    public void TypingTextLeavesTheIndentationAlone()
    {
        var editor = new TextEditor
        {
            Text = "\t  parent\nchild",
            IndentationStrategy = new DefaultIndentationStrategy()
        };
        editor.CaretOffset = editor.Document.TextLength;

        editor.TextArea.PerformTextInput("!");

        Assert.AreEqual("\t  parent\nchild!", editor.Text);
    }

    [TestMethod]
    public void CompletionSessionFiltersByTypedPrefixAndCompletesSelection()
    {
        var editor = new TextEditor { Text = "Con" };
        editor.CaretOffset = editor.Document.TextLength;
        var session = new CompletionSession(editor, 0);
        session.SetItems([
            new CompletionData("Console", priority: 2),
            new CompletionData("const"),
            new CompletionData("string")]);

        Assert.HasCount(2, session.FilteredItems);
        Assert.AreEqual("Console", session.SelectedItem?.Text);
        Assert.IsTrue(session.Complete());
        Assert.AreEqual("Console", editor.Text);
        Assert.AreEqual(7, editor.CaretOffset);
    }

    [TestMethod]
    public void TextAreaFacadeTracksCaretSelectionAndDocumentSwitches()
    {
        var editor = new TextEditor { Text = "one\ntwo" };
        int changes = 0;
        editor.TextArea.Caret.PositionChanged += (_, _) => changes++;

        editor.Select(4, 2);

        Assert.AreEqual(2, editor.TextArea.Caret.Line);
        Assert.AreEqual(3, editor.TextArea.Caret.Column);
        Assert.AreEqual(2, editor.TextArea.Selection.Segments.Single().Length);
        Assert.AreEqual(1, changes);

        var replacement = new TextDocument("replacement");
        editor.Document = replacement;
        Assert.AreSame(replacement, editor.TextArea.Document);
    }

    [TestMethod]
    public void BraceFoldingStrategyFindsNestedMultilineRegions()
    {
        var document = new TextDocument("class C\n{\n void M()\n {\n }\n}\n");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateNewFoldings(document, out int firstErrorOffset).ToArray();

        Assert.AreEqual(-1, firstErrorOffset);
        Assert.HasCount(2, foldings);
        Assert.IsLessThan(foldings[1].StartOffset, foldings[0].StartOffset);
    }

    /// <summary>
    /// A folded section stands in one line: the element covers the whole range, so the lines it
    /// swallows are gone from the surface and the caret still resolves inside them.
    /// </summary>
    [TestMethod]
    public void AFoldedSectionCollapsesIntoOneLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var editor = new TextEditor { Text = "class A\n{\n    int x;\n}\nafter\n", SkipViewportCull = true };
        var manager = FoldingManager.Install(editor);
        editor.Measure(new Size(360, 200));
        editor.Arrange(new Rect(0, 0, 360, 200));
        int before = editor.TextArea.TextView.Host.VisibleTextLines.Count;

        manager.UpdateFoldings([new NewFolding(8, 22) { DefaultClosed = true }], -1);
        editor.Measure(new Size(360, 200));
        editor.Arrange(new Rect(0, 0, 360, 200));

        var lines = editor.TextArea.TextView.Host.VisibleTextLines;
        Assert.IsLessThan(before, lines.Count, "Folding hid no line.");
        var covering = lines.Single(line => line.LogicalLine.LineNumber == 1);
        Assert.IsGreaterThanOrEqualTo(22, covering.LogicalLine.Offset + covering.LogicalLine.Length,
            "The fold element did not make its line reach the end of the folded range.");

        manager.AllFoldings.Single().IsFolded = false;
        editor.Measure(new Size(360, 200));
        editor.Arrange(new Rect(0, 0, 360, 200));

        Assert.AreEqual(before, editor.TextArea.TextView.Host.VisibleTextLines.Count,
            "Unfolding did not bring the hidden lines back.");
        FoldingManager.Uninstall(manager);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void FoldingRefreshReusesLargeExistingSetWithoutQuadraticLookup()
    {
        var editor = new TextEditor { Text = new string('x', 120_000) };
        var manager = FoldingManager.Install(editor);
        var foldings = Enumerable.Range(0, 10_000)
            .Select(static index => new NewFolding(index * 10, index * 10 + 5))
            .ToArray();
        manager.UpdateFoldings(foldings, -1);

        var stopwatch = Stopwatch.StartNew();
        manager.UpdateFoldings(foldings, -1);

        Assert.IsLessThan(500L, stopwatch.ElapsedMilliseconds,
            $"Updating 10,000 existing foldings took {stopwatch.ElapsedMilliseconds}ms.");
        Assert.AreEqual(10_000, manager.AllFoldings.Count());
        FoldingManager.Uninstall(manager);
    }
}
