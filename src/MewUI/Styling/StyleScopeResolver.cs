using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal static class StyleScopeResolver
{
    internal static Style? Resolve(
        Control control,
        string? styleName,
        StyleSheet? applicationStyleSheet)
    {
        ArgumentNullException.ThrowIfNull(control);

        Type controlType = control.GetType();
        for (Element? current = control; current != null; current = current.ContextParent)
        {
            if (current is not FrameworkElement { StyleSheet: { } sheet })
            {
                continue;
            }

            Style? style = styleName != null
                ? sheet.Get(styleName)
                : sheet.GetByType(controlType);
            if (style != null)
            {
                return style;
            }
        }

        return styleName != null
            ? applicationStyleSheet?.Get(styleName)
            : applicationStyleSheet?.GetByType(controlType);
    }

    internal static string DescribeScopes(Control control, bool includesApplication)
    {
        var scopes = new List<string>(capacity: 4);
        for (Element? current = control; current != null; current = current.ContextParent)
        {
            if (current is FrameworkElement { StyleSheet: not null })
            {
                string suffix = ReferenceEquals(current, control) ? " (self)" : string.Empty;
                scopes.Add(current.GetType().Name + suffix);
            }
        }

        if (includesApplication)
        {
            scopes.Add(nameof(Application));
        }

        return scopes.Count == 0 ? "(none)" : string.Join(" -> ", scopes);
    }
}
