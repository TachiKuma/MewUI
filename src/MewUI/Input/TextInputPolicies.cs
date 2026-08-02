using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

internal static class TextInputPolicies
{
    internal static bool AllowsTabTextInput(UIElement? focusedElement)
        => focusedElement is LegacyTextBase tb && tb.AcceptTab && !tb.IsReadOnly;
}

