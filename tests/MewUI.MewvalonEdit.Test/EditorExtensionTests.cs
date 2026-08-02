using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.CodeCompletion;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Search;
using Aprillz.MewUI.MewvalonEdit.Folding;

namespace Aprillz.MewUI.MewvalonEdit.Test;

[TestClass]
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

        SearchPanel.Uninstall(search);
    }

    [TestMethod]
    public void DefaultIndentationCopiesPreviousLineWhitespace()
    {
        var document = new TextDocument("\t  parent\nchild");
        var strategy = new DefaultIndentationStrategy();

        strategy.IndentLine(document, document.GetLineByNumber(2));

        Assert.AreEqual("\t  parent\n\t  child", document.Text);
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
}
