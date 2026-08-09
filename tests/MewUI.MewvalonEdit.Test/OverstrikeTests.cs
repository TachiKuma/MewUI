using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// Overstrike mode types over what is in front of the caret instead of pushing it along, except
/// where there is nothing to take the place of: the end of a line, and a line ending itself.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class OverstrikeTests
{
    [TestMethod]
    public void TypingTakesThePlaceOfTheCharacterInFront()
    {
        var editor = CreateEditor("abc");
        editor.TextArea.OverstrikeMode = true;
        editor.CaretOffset = 0;

        editor.TextArea.PerformTextInput("X");

        Assert.AreEqual("Xbc", editor.Text);
        Assert.AreEqual(1, editor.CaretOffset);
    }

    [TestMethod]
    public void TypingAtTheEndOfALineStillInserts()
    {
        var editor = CreateEditor("ab\ncd");
        editor.TextArea.OverstrikeMode = true;
        editor.CaretOffset = 2;

        editor.TextArea.PerformTextInput("X");

        Assert.AreEqual("abX\ncd", editor.Text, "there is nothing at the end of a line to take the place of");
    }

    [TestMethod]
    public void ALineEndingIsAlwaysInserted()
    {
        var editor = CreateEditor("abc");
        editor.TextArea.OverstrikeMode = true;
        editor.CaretOffset = 1;

        editor.TextArea.PerformTextInput("\n");

        Assert.AreEqual("a\nbc", editor.Text);
    }

    [TestMethod]
    public void InsertTogglesTheModeOnlyWhenTheOptionsAllowIt()
    {
        var editor = CreateEditor("abc");

        Press(editor, Key.Insert);
        Assert.IsFalse(editor.TextArea.OverstrikeMode, "the switch is off until a host asks for it");

        editor.Options.AllowToggleOverstrikeMode = true;
        Press(editor, Key.Insert);
        Assert.IsTrue(editor.TextArea.OverstrikeMode);

        Press(editor, Key.Insert);
        Assert.IsFalse(editor.TextArea.OverstrikeMode);
    }

    private static void Press(TextEditor editor, Key key)
        => editor.TextArea.HandleKeyDown(new KeyEventArgs(key, platformKey: 0, ModifierKeys.None));

    private static TextEditor CreateEditor(string text)
    {
        var editor = new TextEditor { Text = text, SkipViewportCull = true };
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));
        return editor;
    }
}
