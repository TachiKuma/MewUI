namespace Aprillz.MewUI;

/// <summary>
/// Universal editing commands shared by keyboard defaults, menus and toolbars; controls provide
/// handlers via their <see cref="Controls.Element.Commands"/> scope.
/// </summary>
public static class StandardCommands
{
    public static Command Cut { get; } = new("edit.cut", "Cut");

    public static Command Copy { get; } = new("edit.copy", "Copy");

    public static Command Paste { get; } = new("edit.paste", "Paste");

    public static Command Delete { get; } = new("edit.delete", "Delete");

    public static Command Undo { get; } = new("edit.undo", "Undo");

    public static Command Redo { get; } = new("edit.redo", "Redo");

    public static Command SelectAll { get; } = new("edit.selectAll", "Select All");
}
