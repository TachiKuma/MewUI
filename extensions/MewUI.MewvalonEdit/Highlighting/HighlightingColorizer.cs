using System.Text.RegularExpressions;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

/// <param name="definition">Rule set whose regex matches become paint spans.</param>
/// <param name="isDarkTheme">Queried per line so a theme switch repaints without rebuilding the colorizer. Defaults to dark.</param>
public class HighlightingColorizer(IHighlightingDefinition definition, Func<bool>? isDarkTheme = null)
    : DocumentColorizingTransformer
{
    public IHighlightingDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    protected override void ColorizeLine(DocumentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (CurrentContext is not ITextRunConstructionContext context)
        {
            return;
        }

        bool isDark = isDarkTheme?.Invoke() ?? true;
        string text = context.Document.GetText(line.Offset, line.Length);
        foreach (var rule in Definition.MainRuleSet.Rules)
        {
            foreach (Match match in rule.Regex.Matches(text))
            {
                if (!match.Success || match.Length == 0) continue;
                ApplyColorToElement(line.Offset + match.Index, match.Length, rule.Color, isDark);
            }
        }
    }

    /// <summary>Applies one highlighting color to a document range. Override to adjust how colors reach the view.</summary>
    protected virtual void ApplyColorToElement(int offset, int length, HighlightingColor color, bool isDarkTheme)
    {
        ArgumentNullException.ThrowIfNull(color);
        ChangeLinePart(offset, offset + length, element =>
        {
            var properties = element.TextRunProperties;
            if (color.ResolveForeground(isDarkTheme) is Color foreground)
            {
                properties.SetForegroundBrush(foreground);
            }
            if (color.ResolveBackground(isDarkTheme) is Color background)
            {
                properties.SetBackgroundBrush(background);
            }
            var decoration = TextDecoration.None;
            if (color.Underline == true) decoration |= TextDecoration.Underline;
            if (color.Strikethrough == true) decoration |= TextDecoration.Strikethrough;
            if (decoration != TextDecoration.None)
            {
                properties.SetTextDecorations(decoration);
            }
            if (color.FontWeight is FontWeight weight)
            {
                properties.SetTypeface(new Typeface(color.FontFamily ?? string.Empty, weight));
            }
        });
    }
}
