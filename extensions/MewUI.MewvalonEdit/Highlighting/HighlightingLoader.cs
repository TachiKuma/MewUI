using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

/// <summary>Raised when an xshd document cannot be turned into a definition.</summary>
public sealed class HighlightingDefinitionInvalidException(string message) : Exception(message);

/// <summary>
/// Loads AvalonEdit .xshd syntax definitions. Colors, keyword lists, rules, and spans are read;
/// span rule sets are flattened into the main set because this port keeps a single active rule set.
/// </summary>
public static class HighlightingLoader
{
    private const string XSHD_NAMESPACE = "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008";

    public static IHighlightingDefinition Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        XDocument document;
        try
        {
            document = XDocument.Load(reader);
        }
        catch (System.Xml.XmlException error)
        {
            throw new HighlightingDefinitionInvalidException($"The syntax definition is not valid XML: {error.Message}");
        }
        return Load(document);
    }

    public static IHighlightingDefinition Load(string xshd)
    {
        ArgumentNullException.ThrowIfNull(xshd);
        using var reader = new StringReader(xshd);
        return Load(reader);
    }

    private static IHighlightingDefinition Load(XDocument document)
    {
        var root = document.Root
            ?? throw new HighlightingDefinitionInvalidException("The syntax definition is empty.");
        XNamespace ns = root.Name.Namespace == XNamespace.None ? XSHD_NAMESPACE : root.Name.Namespace;
        string name = (string?)root.Attribute("name")
            ?? throw new HighlightingDefinitionInvalidException("The syntax definition has no name.");

        var definition = new HighlightingDefinition(name);
        var colors = new Dictionary<string, HighlightingColor>(StringComparer.Ordinal);
        foreach (var element in root.Elements(ns + "Color"))
        {
            string? colorName = (string?)element.Attribute("name");
            var color = ReadColor(element);
            if (colorName is not null)
            {
                colors[colorName] = color;
                definition.AddColor(colorName, color);
            }
        }

        foreach (var ruleSet in root.Elements(ns + "RuleSet"))
        {
            LoadRuleSet(definition.MainRuleSet, ruleSet, ns, colors);
        }
        return definition;
    }

    private static void LoadRuleSet(
        HighlightingRuleSet target,
        XElement ruleSet,
        XNamespace ns,
        Dictionary<string, HighlightingColor> colors)
    {
        foreach (var element in ruleSet.Elements())
        {
            if (element.Name == ns + "Keywords")
            {
                var color = ResolveColor(element, colors);
                var words = element.Elements(ns + "Word")
                    .Select(word => Regex.Escape(word.Value.Trim()))
                    .Where(word => word.Length > 0)
                    .ToArray();
                if (words.Length > 0)
                {
                    target.Rules.Add(CreateRule($@"\b(?:{string.Join('|', words)})\b", color, RegexOptions.None));
                }
            }
            else if (element.Name == ns + "Rule")
            {
                // Element bodies are written free-form with comments, as AvalonEdit's loader assumes.
                string pattern = element.Value.Trim();
                if (pattern.Length > 0)
                {
                    target.Rules.Add(CreateRule(
                        pattern, ResolveColor(element, colors), RegexOptions.IgnorePatternWhitespace));
                }
            }
            else if (element.Name == ns + "Span")
            {
                LoadSpan(target, element, ns, colors);
            }
        }
    }

    private static HighlightingRule CreateRule(string pattern, HighlightingColor color, RegexOptions options)
        => new()
        {
            Regex = new Regex(pattern, RegexOptions.CultureInvariant | options),
            Color = color
        };

    private static void LoadSpan(
        HighlightingRuleSet target,
        XElement span,
        XNamespace ns,
        Dictionary<string, HighlightingColor> colors)
    {
        var beginElement = span.Element(ns + "Begin");
        string? begin = beginElement?.Value.Trim() ?? (string?)span.Attribute("begin");
        if (string.IsNullOrEmpty(begin))
        {
            return;
        }
        var endElement = span.Element(ns + "End");
        string? end = endElement?.Value.Trim() ?? (string?)span.Attribute("end");
        var color = ResolveColor(span, colors);

        // Element bodies are written free-form with comments; attribute forms are plain patterns.
        var options = beginElement is not null || endElement is not null
            ? RegexOptions.IgnorePatternWhitespace
            : RegexOptions.None;

        if (string.IsNullOrEmpty(end))
        {
            // A span without an end runs to the end of the line, which a plain rule expresses. Its
            // nested sets go with it: one rule paints the whole run, so nothing could apply inside.
            target.Rules.Add(CreateRule($"(?:{begin}).*$", color, options));
            return;
        }

        var created = new HighlightingSpan
        {
            StartExpression = new Regex(begin, RegexOptions.CultureInvariant | options),
            EndExpression = new Regex(end, RegexOptions.CultureInvariant | options),
            SpanColor = color
        };
        var nestedSets = span.Elements(ns + "RuleSet").ToArray();
        if (nestedSets.Length > 0)
        {
            // Kept on the span rather than folded into the enclosing set, or its rules would also
            // colour text outside the span.
            var nested = new HighlightingRuleSet();
            foreach (var element in nestedSets)
            {
                LoadRuleSet(nested, element, ns, colors);
            }
            created.RuleSet = nested;
        }
        target.Spans.Add(created);
    }

    private static HighlightingColor ResolveColor(XElement element, Dictionary<string, HighlightingColor> colors)
    {
        string? reference = (string?)element.Attribute("color");
        if (reference is not null && colors.TryGetValue(reference, out var known))
        {
            return known;
        }
        return ReadColor(element);
    }

    private static HighlightingColor ReadColor(XElement element)
    {
        var color = new HighlightingColor();
        if (ParseColor((string?)element.Attribute("foreground")) is Color foreground)
        {
            color.Foreground = foreground;
            color.LightForeground = foreground;
        }
        if (ParseColor((string?)element.Attribute("background")) is Color background)
        {
            color.Background = background;
            color.LightBackground = background;
        }
        if ((string?)element.Attribute("fontWeight") is string weight)
        {
            color.FontWeight = weight.Equals("bold", StringComparison.OrdinalIgnoreCase)
                ? Aprillz.MewUI.FontWeight.Bold
                : Aprillz.MewUI.FontWeight.Normal;
        }
        if ((string?)element.Attribute("underline") is string underline)
        {
            color.Underline = bool.TryParse(underline, out bool value) && value;
        }
        if ((string?)element.Attribute("strikethrough") is string strikethrough)
        {
            color.Strikethrough = bool.TryParse(strikethrough, out bool value) && value;
        }
        return color;
    }

    private static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        value = value.Trim();
        if (value.StartsWith('#'))
        {
            return Color.FromHex(value);
        }
        return NamedColors.TryGetValue(value, out var named) ? named : null;
    }

    // xshd files use the WPF color names; only the ones the bundled definitions rely on are mapped.
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Black"] = Color.FromRgb(0, 0, 0),
        ["White"] = Color.FromRgb(255, 255, 255),
        ["Red"] = Color.FromRgb(255, 0, 0),
        ["Green"] = Color.FromRgb(0, 128, 0),
        ["Blue"] = Color.FromRgb(0, 0, 255),
        ["Navy"] = Color.FromRgb(0, 0, 128),
        ["Teal"] = Color.FromRgb(0, 128, 128),
        ["Olive"] = Color.FromRgb(128, 128, 0),
        ["Brown"] = Color.FromRgb(165, 42, 42),
        ["Maroon"] = Color.FromRgb(128, 0, 0),
        ["Magenta"] = Color.FromRgb(255, 0, 255),
        ["Pink"] = Color.FromRgb(255, 192, 203),
        ["DeepPink"] = Color.FromRgb(255, 20, 147),
        ["Gray"] = Color.FromRgb(128, 128, 128),
        ["DarkGray"] = Color.FromRgb(169, 169, 169),
        ["DarkBlue"] = Color.FromRgb(0, 0, 139),
        ["DarkCyan"] = Color.FromRgb(0, 139, 139),
        ["DarkGreen"] = Color.FromRgb(0, 100, 0),
        ["MidnightBlue"] = Color.FromRgb(25, 25, 112),
        ["SaddleBrown"] = Color.FromRgb(139, 69, 19),
        ["Orange"] = Color.FromRgb(255, 165, 0),
        ["Purple"] = Color.FromRgb(128, 0, 128)
    };
}
