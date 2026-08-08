using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Input;

/// <summary>
/// A key binding on an element answers for that element's subtree: it fires while a key bubbles
/// through, after the element's own handling, so a shortcut never takes a key the focused control
/// uses. <see cref="Window.KeyBindings"/> stays the window-wide variant checked after bubbling.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ElementKeyBindingTests
{
    [TestMethod]
    public void ABindingOnAnAncestorAnswersAKeyTheFocusedControlLeftAlone()
    {
        var window = HeadlessWindow.Create();
        var box = new TextBox();
        var host = new Border { Child = box };
        int fired = 0;
        host.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.F3), () => fired++));
        window.Content = host;
        window.PerformLayout();
        box.Focus();

        window.SendKeyDown(Key.F3);

        Assert.AreEqual(1, fired, "The key bubbled through the host unanswered.");
    }

    [TestMethod]
    public void TheFocusedControlKeepsTheKeysItUses()
    {
        var window = HeadlessWindow.Create();
        var box = new TextBox { Text = "abc" };
        var host = new Border { Child = box };
        int fired = 0;
        // Left arrow moves the caret inside a text box; a subtree shortcut must not take it.
        host.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.Left), () => fired++));
        window.Content = host;
        window.PerformLayout();
        box.Focus();
        box.CaretPosition = 2;

        window.SendKeyDown(Key.Left);

        Assert.AreEqual(0, fired, "The shortcut took a key the focused control uses.");
        Assert.AreEqual(1, box.CaretPosition);
    }

    [TestMethod]
    public void TheDeepestBindingWins()
    {
        var window = HeadlessWindow.Create();
        var box = new TextBox();
        var inner = new Border { Child = box };
        var outer = new Border { Child = inner };
        var log = new List<string>();
        inner.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.F5), () => log.Add("inner")));
        outer.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.F5), () => log.Add("outer")));
        window.Content = outer;
        window.PerformLayout();
        box.Focus();

        window.SendKeyDown(Key.F5);

        CollectionAssert.AreEqual(new[] { "inner" }, log);
    }

    [TestMethod]
    public void ADeclinedBindingLetsTheKeyBubbleOn()
    {
        var window = HeadlessWindow.Create();
        var box = new TextBox();
        var inner = new Border { Child = box };
        var outer = new Border { Child = inner };
        var log = new List<string>();
        inner.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.F5), () => log.Add("inner"), () => false));
        outer.KeyBindings.Add(new KeyBinding(new KeyGesture(Key.F5), () => log.Add("outer")));
        window.Content = outer;
        window.PerformLayout();
        box.Focus();

        window.SendKeyDown(Key.F5);

        CollectionAssert.AreEqual(new[] { "outer" }, log,
            "A binding whose CanExecute declined must leave the key to the elements above.");
    }
}
