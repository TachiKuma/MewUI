using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// Target-preservation coverage: menus resolve and execute command items against the context
/// captured when they opened, not against the popup's own focus.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MenuCommandTargetTests
{
    [TestMethod]
    public void ContextMenu_ExecutesAgainstCapturedOwner()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();
        window.FocusManager.SetFocus(editor);

        var copyCommand = new Command("edit.copy", "Copy");
        int executed = 0;
        editor.Commands.Bind(copyCommand, () => executed++);

        var menu = new ContextMenu();
        menu.AddEntry(new MenuItem(copyCommand));
        menu.ShowAt(editor, new Point(100, 100));
        window.PerformLayout();

        Assert.AreNotSame(editor, window.FocusManager.FocusedElement, "the open menu takes focus");

        var bounds = menu.Bounds;
        window.SendClick(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));

        Assert.AreEqual(1, executed, "the item executes against the captured editor target");
    }

    [TestMethod]
    public void ContextMenu_EnabledStateComesFromCapturedTarget()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();

        var copyCommand = new Command("edit.copy", "Copy");
        bool hasSelection = false;
        editor.Commands.Bind(copyCommand, static () => { }, () => hasSelection);

        var item = new MenuItem(copyCommand);
        var menu = new ContextMenu();
        menu.AddEntry(item);

        menu.ShowAt(editor, new Point(100, 100));
        window.PerformLayout();
        Assert.IsFalse(item.IsEnabled, "menu open queries CanExecute against the owner");
        menu.CloseTree(window);

        hasSelection = true;
        menu.ShowAt(editor, new Point(100, 100));
        window.PerformLayout();
        Assert.IsTrue(item.IsEnabled, "reopening re-queries current state");
    }

    [TestMethod]
    public void MenuItem_UsesCommandTextAndEffectiveShortcut()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();

        var copyCommand = new Command("edit.copy", "Copy");
        editor.Commands.Bind(copyCommand, static () => { });
        var gesture = new KeyGesture(Key.C, ModifierKeys.Control);
        window.InputMap.Bind(copyCommand, gesture);

        var item = new MenuItem(copyCommand);
        var menu = new ContextMenu();
        menu.AddEntry(item);
        menu.ShowAt(editor, new Point(100, 100));
        window.PerformLayout();

        Assert.AreEqual("Copy", item.GetParsedText().displayText, "Command.Text supplies the label");
        Assert.AreEqual(gesture.ToDisplayString(), item.GetShortcutDisplayText(), "shortcut label is the effective input-map gesture");
    }

    [TestMethod]
    public void OpenMenu_ReflectsStateChangeThroughEvaluationPass()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();

        var copyCommand = new Command("edit.copy", "Copy");
        bool hasSelection = false;
        editor.Commands.Bind(copyCommand, static () => { }, () => hasSelection);

        var item = new MenuItem(copyCommand);
        var menu = new ContextMenu();
        menu.AddEntry(item);
        menu.ShowAt(editor, new Point(100, 100));
        window.PerformLayout();
        Assert.IsFalse(item.IsEnabled);

        hasSelection = true;
        window.EvaluateCommandStates();

        Assert.IsTrue(item.IsEnabled, "the open menu is a tracked command source");
    }
}
