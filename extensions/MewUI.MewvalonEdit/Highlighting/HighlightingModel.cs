using System.Text.RegularExpressions;
using Aprillz.MewUI;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

public interface IHighlightingDefinition
{
    string Name { get; }
    HighlightingRuleSet MainRuleSet { get; }
    HighlightingRuleSet? GetNamedRuleSet(string name);
    HighlightingColor? GetNamedColor(string name);
    IEnumerable<HighlightingColor> NamedHighlightingColors { get; }
    IDictionary<string, string> Properties { get; }
}

public sealed class HighlightingColor
{
    public string? Name { get; set; }
    public Color? Foreground { get; set; }
    public Color? Background { get; set; }

    /// <summary>Foreground used on light themes. Falls back to <see cref="Foreground"/> when unset.</summary>
    public Color? LightForeground { get; set; }

    /// <summary>Background used on light themes. Falls back to <see cref="Background"/> when unset.</summary>
    public Color? LightBackground { get; set; }

    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public FontWeight? FontWeight { get; set; }
    public bool? Underline { get; set; }
    public bool? Strikethrough { get; set; }

    internal Color? ResolveForeground(bool isDark) => isDark ? Foreground : LightForeground ?? Foreground;

    internal Color? ResolveBackground(bool isDark) => isDark ? Background : LightBackground ?? Background;
}

public sealed class HighlightingRule
{
    public Regex Regex { get; set; } = null!;
    public HighlightingColor Color { get; set; } = null!;
}

public sealed class HighlightingRuleSet
{
    public string? Name { get; set; }
    public IList<HighlightingRule> Rules { get; } = new List<HighlightingRule>();
}

public sealed class HighlightingDefinition(string name) : IHighlightingDefinition
{
    private readonly Dictionary<string, HighlightingRuleSet> _ruleSets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HighlightingColor> _colors = new(StringComparer.Ordinal);

    public string Name { get; } = name;
    public HighlightingRuleSet MainRuleSet { get; } = new();
    public IEnumerable<HighlightingColor> NamedHighlightingColors => _colors.Values;
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public HighlightingDefinition AddColor(string name, HighlightingColor color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(color);
        color.Name = name;
        _colors[name] = color;
        return this;
    }

    public HighlightingDefinition AddRule(string pattern, HighlightingColor color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(color);
        MainRuleSet.Rules.Add(new HighlightingRule
        {
            Regex = new Regex(pattern, RegexOptions.CultureInvariant),
            Color = color
        });
        return this;
    }

    public HighlightingDefinition AddRuleSet(string name, HighlightingRuleSet ruleSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ruleSet.Name = name;
        _ruleSets[name] = ruleSet;
        return this;
    }

    public HighlightingRuleSet? GetNamedRuleSet(string name)
        => _ruleSets.GetValueOrDefault(name);

    public HighlightingColor? GetNamedColor(string name)
        => _colors.GetValueOrDefault(name);
}
