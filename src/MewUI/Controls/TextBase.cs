using Aprillz.MewUI.Input;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for text input controls built on the managed text engine.
/// </summary>
// Rebuilt hierarchy (agent/textBase/plan.md): shared editing members migrate here from
// MultiLineTextBox slice by slice; LegacyTextBase remains frozen until TextBox and
// PasswordBox move onto this base.
public abstract class TextBase : Control
{
    public static readonly MewProperty<ImeMode> ImeModeProperty =
        MewProperty<ImeMode>.Register<TextBase>(nameof(ImeMode), ImeMode.Auto);

    // Shared editing state: derived controls access the document/session directly, matching
    // the field names they used before the extraction.
    private protected readonly EditableTextDocument _document;
    private protected readonly TextEditorSession _editor;

    static TextBase()
    {
        FocusableProperty.OverrideDefaultValue<TextBase>(true);
    }

    protected TextBase()
        : this(new EditableTextDocument())
    {
    }

    protected TextBase(EditableTextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _editor = new TextEditorSession(_document);
        Cursor = CursorType.IBeam;
    }

    /// <summary>
    /// Gets or sets the IME mode for this text control.
    /// </summary>
    public ImeMode ImeMode
    {
        get => GetValue(ImeModeProperty);
        set => SetValue(ImeModeProperty, value);
    }
}
