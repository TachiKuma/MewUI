using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;

namespace Aprillz.MewUI.MewvalonEdit.Test;

/// <summary>
/// Grouping is what the stack is mostly used for: a routine that edits line by line would otherwise
/// cost one undo step per line.
/// </summary>
[TestClass]
public sealed class UndoStackTests
{
    [TestMethod]
    public void TheStackBelongsToTheDocument()
    {
        var document = new TextDocument("abc");
        Assert.AreSame(document.UndoStack, document.UndoStack);
    }

    [TestMethod]
    public void AGroupUndoesAsOneStep()
    {
        var editor = new TextEditor { Text = "one\ntwo" };
        var stack = editor.Document.UndoStack;

        using (stack.OpenUndoGroup())
        {
            editor.Document.Insert(0, "a");
            editor.Document.Insert(0, "b");
            editor.Document.Insert(0, "c");
        }
        Assert.AreEqual("cba" + "one\ntwo", editor.Document.Text);

        Assert.IsTrue(stack.Undo());

        Assert.AreEqual("one\ntwo", editor.Document.Text);
        Assert.IsFalse(stack.CanUndo);
    }

    [TestMethod]
    public void CanUndoFollowsTheDocument()
    {
        var editor = new TextEditor { Text = "abc" };
        var stack = editor.Document.UndoStack;
        Assert.IsFalse(stack.CanUndo);

        editor.Document.Insert(0, "x");

        Assert.IsTrue(stack.CanUndo);
        Assert.IsFalse(stack.CanRedo);

        stack.Undo();

        Assert.IsFalse(stack.CanUndo);
        Assert.IsTrue(stack.CanRedo);
    }

    [TestMethod]
    public void RedoPutsTheWholeGroupBack()
    {
        var editor = new TextEditor { Text = string.Empty };
        var stack = editor.Document.UndoStack;
        using (stack.OpenUndoGroup())
        {
            editor.Document.Insert(0, "a");
            editor.Document.Insert(1, "b");
        }
        stack.Undo();

        Assert.IsTrue(stack.Redo());

        Assert.AreEqual("ab", editor.Document.Text);
        Assert.IsFalse(stack.CanRedo);
    }

    [TestMethod]
    public void EndingAGroupThatWasNeverStartedThrows()
    {
        var document = new TextDocument("abc");
        Assert.ThrowsExactly<InvalidOperationException>(() => document.UndoStack.EndUndoGroup());
    }

    [TestMethod]
    public void NestingExtendsTheOutermostGroup()
    {
        var editor = new TextEditor { Text = string.Empty };
        var stack = editor.Document.UndoStack;

        stack.StartUndoGroup();
        editor.Document.Insert(0, "a");
        stack.StartUndoGroup();
        Assert.IsTrue(stack.IsInUndoGroup);
        editor.Document.Insert(1, "b");
        stack.EndUndoGroup();
        Assert.IsTrue(stack.IsInUndoGroup, "The outer group is still open.");
        editor.Document.Insert(2, "c");
        stack.EndUndoGroup();

        Assert.IsFalse(stack.IsInUndoGroup);
        stack.Undo();
        Assert.AreEqual(string.Empty, editor.Document.Text);
    }

    [TestMethod]
    public void ClearAllLeavesTheTextAlone()
    {
        var editor = new TextEditor { Text = "abc" };
        editor.Document.Insert(0, "x");

        editor.Document.UndoStack.ClearAll();

        Assert.IsFalse(editor.Document.UndoStack.CanUndo);
        Assert.AreEqual("xabc", editor.Document.Text);
    }

    [TestMethod]
    public void TheSizeLimitReachesTheDocument()
    {
        var document = new TextDocument("abc");
        document.UndoStack.SizeLimit = 5;
        Assert.AreEqual(5, document.UndoStack.SizeLimit);
    }

    [TestMethod]
    public void ADeclaredChangeBlockGroupsWhatIsInsideIt()
    {
        var editor = new TextEditor { Text = string.Empty };

        using (editor.DeclareChangeBlock())
        {
            editor.Document.Insert(0, "a");
            editor.Document.Insert(1, "b");
        }
        editor.Document.UndoStack.Undo();

        Assert.AreEqual(string.Empty, editor.Document.Text);
    }

    [TestMethod]
    public void RunUpdateGroupsWhatItRuns()
    {
        var document = new TextEditor { Text = string.Empty }.Document;

        document.RunUpdate(() =>
        {
            document.Insert(0, "a");
            document.Insert(1, "b");
        });
        document.UndoStack.Undo();

        Assert.AreEqual(string.Empty, document.Text);
    }

    /// <summary>
    /// The case the group was measured against: indenting a block edits every line in it, and
    /// undoing it a line at a time is not what the caller asked for.
    /// </summary>
    [TestMethod]
    public void IndentingABlockUndoesAsOneStep()
    {
        var document = new TextEditor { Text = "a\n    b\nc\nd" }.Document;
        var strategy = new DefaultIndentationStrategy();

        strategy.IndentLines(document, 2, 4);
        string indented = document.Text;
        Assert.AreNotEqual("a\n    b\nc\nd", indented, "The strategy changed nothing to undo.");

        document.UndoStack.Undo();

        Assert.AreEqual("a\n    b\nc\nd", document.Text);
        Assert.IsFalse(document.UndoStack.CanUndo);
    }
}
