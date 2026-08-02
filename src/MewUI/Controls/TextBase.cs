namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for text input controls built on the managed text engine.
/// </summary>
// Rebuilt hierarchy (agent/textBase/plan.md): shared editing members migrate here from
// MultiLineTextBox slice by slice; LegacyTextBase remains frozen until TextBox and
// PasswordBox move onto this base.
public abstract class TextBase : Control
{
    static TextBase()
    {
        FocusableProperty.OverrideDefaultValue<TextBase>(true);
    }

    protected TextBase()
    {
        Cursor = CursorType.IBeam;
    }
}
