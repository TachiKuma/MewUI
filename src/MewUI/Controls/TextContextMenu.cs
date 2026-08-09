using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Builds and shows the shared default context menu for text surfaces from semantic commands.
/// </summary>
internal static class TextContextMenu
{
    internal static void Show(
        ContextMenu menu,
        UIElement owner,
        Point positionInWindow,
        params Command[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        menu.Items.Clear();
        bool hasItems = false;

        hasItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuUndo.Value, StandardCommands.Undo, commands);
        hasItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuRedo.Value, StandardCommands.Redo, commands);

        if (hasItems && ContainsAny(commands, StandardCommands.Cut, StandardCommands.Copy, StandardCommands.Paste))
        {
            menu.AddSeparator();
        }

        bool hasClipboardItems = false;
        hasClipboardItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuCut.Value, StandardCommands.Cut, commands);
        hasClipboardItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuCopy.Value, StandardCommands.Copy, commands);
        hasClipboardItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuPaste.Value, StandardCommands.Paste, commands);
        hasItems |= hasClipboardItems;

        if (hasItems && commands.Contains(StandardCommands.SelectAll))
        {
            menu.AddSeparator();
        }

        hasItems |= AddItem(menu, MewUIStrings.TextBoxContextMenuSelectAll.Value, StandardCommands.SelectAll, commands);

        if (hasItems)
        {
            menu.ShowAt(owner, positionInWindow);
        }
    }

    private static bool AddItem(ContextMenu menu, string header, Command command, Command[] commands)
    {
        if (!commands.Contains(command))
        {
            return false;
        }

        menu.AddItem(header, command);
        return true;
    }

    private static bool ContainsAny(Command[] commands, params Command[] candidates)
        => candidates.Any(commands.Contains);
}
