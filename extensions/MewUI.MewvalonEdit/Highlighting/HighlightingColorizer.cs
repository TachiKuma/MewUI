using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

public sealed class HighlightingColorizer(IHighlightingDefinition definition) : ITextClassifier
{
    public IHighlightingDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
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
                    color.Foreground,
                    color.Background,
                    decoration));
            }
        }
    }
}
