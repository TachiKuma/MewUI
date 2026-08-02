using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

/// <summary>A default text context menu entry: the action to run and whether it is enabled.</summary>
internal readonly record struct TextMenuCommand(Action Execute, bool IsEnabled);

/// <summary>
/// Builds and shows the shared default context menu for text surfaces.
/// Editors pass all commands; read-only viewers pass only copy/selectAll and the
/// editing entries are omitted entirely.
/// </summary>
internal static class TextContextMenu
{
    internal static void Show(
        ContextMenu menu,
        UIElement owner,
        Point positionInWindow,
        TextMenuCommand? undo = null,
        TextMenuCommand? redo = null,
        TextMenuCommand? cut = null,
        TextMenuCommand? copy = null,
        TextMenuCommand? paste = null,
        TextMenuCommand? selectAll = null)
    {
        menu.Items.Clear();
        var primary = ModifierKeys.Primary;
        bool hasItems = false;

        hasItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuUndo.Value, undo, new KeyGesture(Key.Z, primary));
        hasItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuRedo.Value, redo, new KeyGesture(Key.Y, primary));

        if (hasItems && (cut.HasValue || copy.HasValue || paste.HasValue))
        {
            menu.AddSeparator();
        }

        bool hasClipboardItems = false;
        hasClipboardItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuCut.Value, cut, new KeyGesture(Key.X, primary));
        hasClipboardItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuCopy.Value, copy, new KeyGesture(Key.C, primary));
        hasClipboardItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuPaste.Value, paste, new KeyGesture(Key.V, primary));
        hasItems |= hasClipboardItems;

        if (hasItems && selectAll.HasValue)
        {
            menu.AddSeparator();
        }

        hasItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuSelectAll.Value, selectAll, new KeyGesture(Key.A, primary));

        if (hasItems)
        {
            menu.ShowAt(owner, positionInWindow);
        }
    }

    private static bool AddItem(ContextMenu menu, string header, TextMenuCommand? command, KeyGesture gesture)
    {
        if (!command.HasValue)
        {
            return false;
        }

        menu.AddItem(header, command.Value.Execute, command.Value.IsEnabled, gesture);
        return true;
    }
}
