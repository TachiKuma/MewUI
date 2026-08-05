using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class TextDocumentTests
{
    /// <summary>
    /// A programmatic document edit is undoable, as in AvalonEdit. Editing the core document
    /// straight through is unrecorded and drops the whole history, so the document has to route
    /// its edits through the surface once an editor adopts it.
    /// </summary>
    [TestMethod]
    public void ProgrammaticEditsStayUndoable()
    {
        var editor = new TextEditor { Text = "hello world" };
        editor.CaretOffset = editor.Text.Length;
        editor.TextArea.PerformTextInput("!");

        editor.Document.Replace(0, 5, "bye");

        Assert.AreEqual("bye world!", editor.Text);
        Assert.IsTrue(editor.CanUndo, "The programmatic replace must be undoable.");
        editor.Undo();
        Assert.AreEqual("hello world!", editor.Text, "Undo rolls back only the programmatic replace.");
    }

    /// <summary>The caret rides along with the text instead of landing on a programmatic edit.</summary>
    [TestMethod]
    public void ProgrammaticEditKeepsTheCaretWithItsText()
    {
        var editor = new TextEditor { Text = "hello world" };
        editor.CaretOffset = 9;

        editor.Document.Replace(0, 5, "bye");

        Assert.AreEqual(7, editor.CaretOffset, "Three characters replaced five, so the caret moved back two.");
    }

    [TestMethod]
    public void DocumentUsesAvalonEditOneBasedLocations()
    {
        var document = new TextDocument("one\ntwo");

        Assert.AreEqual(2, document.LineCount);
        Assert.AreEqual(4, document.GetLineByNumber(2).Offset);
        Assert.AreEqual(new TextLocation(2, 2), document.GetLocation(5));
        Assert.AreEqual(5, document.GetOffset(2, 2));
    }

    [TestMethod]
    public void MutationsRaiseDocumentAndTextEvents()
    {
        var document = new TextDocument("abc");
        DocumentChangeEventArgs? change = null;
        int textChanges = 0;
        document.Changed += (_, args) => change = args;
        document.TextChanged += (_, _) => textChanges++;

        document.Replace(1, 1, "XYZ");

        Assert.AreEqual("aXYZc", document.Text);
        Assert.IsNotNull(change);
        Assert.AreEqual(1, change.Offset);
        Assert.AreEqual(1, change.RemovalLength);
        Assert.AreEqual(3, change.InsertionLength);
        Assert.AreEqual(1, textChanges);
    }
}
