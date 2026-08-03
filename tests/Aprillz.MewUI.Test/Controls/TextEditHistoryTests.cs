using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text.Editing;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TextEditHistoryTests
{
    [TestMethod]
    public void UnrecordedDocumentEditClearsUndoHistory()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("hello");
        Assert.IsTrue(session.CanUndo);

        document.Replace(0, 1, string.Empty);

        Assert.IsFalse(session.CanUndo);
        session.Undo();
        Assert.AreEqual("ello", document.ToString());
    }

    [TestMethod]
    public void UndoSurvivesDocumentSwapRoundTrip()
    {
        var box = new MultiLineTextBox();
        box.ReplaceSelection("first");
        var original = box.Document;
        Assert.IsTrue(box.CanUndo);

        box.Document = new EditableTextDocument("second");
        Assert.IsFalse(box.CanUndo);

        box.Document = original;
        Assert.IsTrue(box.CanUndo);
        box.Undo();
        Assert.AreEqual(string.Empty, box.Text);
    }

    [TestMethod]
    public void CompositionIntermediatesKeepHistory()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("ab");
        Assert.IsTrue(session.CanUndo);

        session.BeginComposition();
        session.UpdateComposition("ㅅ");
        session.UpdateComposition("사");
        Assert.IsTrue(session.CanUndo);
        session.CommitComposition();

        Assert.AreEqual("ab사", document.ToString());
        session.Undo();
        Assert.AreEqual("ab", document.ToString());
        session.Undo();
        Assert.AreEqual(string.Empty, document.ToString());
    }

    [TestMethod]
    public void SharedDocumentMergesHistoryAcrossSessions()
    {
        var document = new EditableTextDocument();
        var first = new TextEditorSession(document);
        var second = new TextEditorSession(document);
        first.ReplaceSelection("a");
        second.SetCaret(document.TextLength);
        second.ReplaceSelection("b");
        Assert.AreEqual("ab", document.ToString());

        first.Undo();
        Assert.AreEqual("a", document.ToString());
        first.Undo();
        Assert.AreEqual(string.Empty, document.ToString());
    }
}
