using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.CodeCompletion;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Search;
using Aprillz.MewUI.MewvalonEdit.Folding;
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
