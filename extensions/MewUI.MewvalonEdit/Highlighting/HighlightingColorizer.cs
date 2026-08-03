using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

/// <param name="definition">Rule set whose regex matches become paint spans.</param>
/// <param name="isDarkTheme">Queried per classification so a theme switch repaints without rebuilding the colorizer. Defaults to dark.</param>
public sealed class HighlightingColorizer(IHighlightingDefinition definition, Func<bool>? isDarkTheme = null)
    : ITextClassifier
{
    public IHighlightingDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        bool isDark = isDarkTheme?.Invoke() ?? true;
        string text = context.Text.ToString();
        foreach (var rule in Definition.MainRuleSet.Rules)
        {
            foreach (System.Text.RegularExpressions.Match match in rule.Regex.Matches(text))
            {
                if (!match.Success || match.Length == 0) continue;
                var color = rule.Color;
                TextDecoration decoration = TextDecoration.None;
                if (color.Underline == true) decoration |= TextDecoration.Underline;
                if (color.Strikethrough == true) decoration |= TextDecoration.Strikethrough;
                output.Add(new TextPaintSpan(
                    new TextRange(match.Index, match.Length),
                    color.ResolveForeground(isDark),
                    color.ResolveBackground(isDark),
                    decoration));
            }
        }
    }
}
