namespace Aprillz.MewUI.Controls;

public abstract class MenuEntry
{
    internal MenuEntry() { }
}

public sealed class MenuSeparator : MenuEntry
{
    public static readonly MenuSeparator Instance = new();

    private MenuSeparator() { }

    internal static double MenuSeparatorHeight => 3;
}

public sealed class MenuItem : MenuEntry
{
    private string _text = string.Empty;
    private string? _cachedDisplayText;
    private char _cachedAccessKey;
    private int _cachedUnderlineIndex = -1;
    private Command? _command;
    private IconTemplate? _icon;
    private string? _commandShortcutDisplayText;

    public MenuItem() { }

    public MenuItem(string text) => Text = text ?? string.Empty;

    public MenuItem(Command command) => Command = command ?? throw new ArgumentNullException(nameof(command));

    public MenuItem(string text, Command command)
    {
        Text = text ?? string.Empty;
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// The semantic command this item invokes. A command item takes execution, enabled state and
    /// shortcut display from the command system.
    /// </summary>
    public Command? Command
    {
        get => _command;
        set
        {
            if (_command == value) return;
            _command = value;
            _cachedDisplayText = null;
            _commandShortcutDisplayText = null;
        }
    }

    /// <summary>
    /// Presentation text override; when empty, <see cref="MewUI.Command.Text"/> supplies the label.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;
            _text = value;
            _cachedDisplayText = null;
        }
    }

    /// <summary>
    /// Presentation icon override; when null, <see cref="MewUI.Command.Icon"/> supplies the icon.
    /// </summary>
    public IconTemplate? Icon
    {
        get => _icon;
        set => _icon = value;
    }

    internal IconTemplate? ResolveIconTemplate() => _icon ?? _command?.Icon;

    private string ResolveRawText()
        => _text.Length > 0 ? _text : _command?.Text ?? string.Empty;

    /// <summary>
    /// Returns cached access key parse results. Parsed once per Text/Command change.
    /// </summary>
    internal (string displayText, char accessKey, int underlineIndex) GetParsedText()
    {
        if (_cachedDisplayText != null)
            return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);

        var rawText = ResolveRawText();
        if (AccessKeyHelper.TryParse(rawText, out var key, out var display))
        {
            _cachedAccessKey = key;
            _cachedUnderlineIndex = AccessKeyHelper.GetUnderlineIndex(rawText);
        }
        else
        {
            display = rawText;
            _cachedAccessKey = default;
            _cachedUnderlineIndex = -1;
        }

        _cachedDisplayText = display;
        return (_cachedDisplayText, _cachedAccessKey, _cachedUnderlineIndex);
    }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Returns the cached shortcut display string (e.g. "Ctrl+S"), or null if no shortcut applies.
    /// Command items show the effective input-map gesture resolved when the menu opened.
    /// </summary>
    internal string? GetShortcutDisplayText()
    {
        return _commandShortcutDisplayText;
    }

    /// <summary>
    /// Applies the enabled state and effective shortcut label queried for the presenting menu's
    /// captured target; returns whether either changed.
    /// </summary>
    internal bool ApplyCommandPresentation(bool isEnabled, string? shortcutDisplayText)
    {
        bool changed = IsEnabled != isEnabled || _commandShortcutDisplayText != shortcutDisplayText;
        IsEnabled = isEnabled;
        _commandShortcutDisplayText = shortcutDisplayText;
        return changed;
    }

    public Menu? SubMenu { get; set; }

    public override string ToString() => Text;
}

public sealed class Menu
{
    private readonly List<MenuEntry> _items = new();

    public IList<MenuEntry> Items => _items;

    /// <summary>
    /// Optional per-menu item height override (in DIP). When NaN, the visual presenter chooses a theme-based default.
    /// </summary>
    public double ItemHeight { get; set; } = double.NaN;

    /// <summary>
    /// Optional per-menu item padding override. When null, the visual presenter chooses a theme-based default.
    /// </summary>
    public Thickness? ItemPadding { get; set; }
}
