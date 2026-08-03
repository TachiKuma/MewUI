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

/// <summary>
/// A region delimited by a start and end pattern, which may span lines. While the span is open its
/// <see cref="RuleSet"/> replaces the enclosing one.
/// </summary>
public sealed class HighlightingSpan
{
    public Regex StartExpression { get; set; } = null!;
    public Regex EndExpression { get; set; } = null!;
    public HighlightingRuleSet? RuleSet { get; set; }
    public HighlightingColor? StartColor { get; set; }
    public HighlightingColor? SpanColor { get; set; }
    public HighlightingColor? EndColor { get; set; }

    /// <summary>Whether <see cref="SpanColor"/> also covers the start delimiter. Default true.</summary>
    public bool SpanColorIncludesStart { get; set; } = true;

    /// <summary>Whether <see cref="SpanColor"/> also covers the end delimiter. Default true.</summary>
    public bool SpanColorIncludesEnd { get; set; } = true;
}

public sealed class HighlightingRuleSet
{
    public string? Name { get; set; }
    public IList<HighlightingRule> Rules { get; } = new List<HighlightingRule>();
    public IList<HighlightingSpan> Spans { get; } = new List<HighlightingSpan>();
}

/// <summary>A colored region of one line produced by the highlighting engine.</summary>
public readonly record struct HighlightedSection(int Offset, int Length, HighlightingColor Color);

/// <summary>Highlighting result for one line.</summary>
public sealed class HighlightedLine
{
    public IList<HighlightedSection> Sections { get; } = new List<HighlightedSection>();
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
        => AddRule(pattern, color, RegexOptions.None);

    /// <summary>Adds a rule whose pattern is compiled with extra <paramref name="options"/>; xshd element bodies need <see cref="RegexOptions.IgnorePatternWhitespace"/>.</summary>
    public HighlightingDefinition AddRule(string pattern, HighlightingColor color, RegexOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(color);
        MainRuleSet.Rules.Add(new HighlightingRule
        {
            Regex = new Regex(pattern, RegexOptions.CultureInvariant | options),
            Color = color
        });
        return this;
    }

    /// <summary>Adds a region delimited by <paramref name="start"/> and <paramref name="end"/> that may cross lines.</summary>
    public HighlightingDefinition AddSpan(string start, string end, HighlightingColor color)
        => AddSpan(start, end, color, RegexOptions.None);

    /// <summary>Adds a span whose delimiters are compiled with extra <paramref name="options"/>; xshd element bodies need <see cref="RegexOptions.IgnorePatternWhitespace"/>.</summary>
    public HighlightingDefinition AddSpan(string start, string end, HighlightingColor color, RegexOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        ArgumentException.ThrowIfNullOrWhiteSpace(end);
        ArgumentNullException.ThrowIfNull(color);
        MainRuleSet.Spans.Add(new HighlightingSpan
        {
            StartExpression = new Regex(start, RegexOptions.CultureInvariant | options),
            EndExpression = new Regex(end, RegexOptions.CultureInvariant | options),
            SpanColor = color
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
