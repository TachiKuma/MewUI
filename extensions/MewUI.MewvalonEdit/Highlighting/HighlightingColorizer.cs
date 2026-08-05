using System.Text.RegularExpressions;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

/// <param name="definition">Rule set whose regex matches become paint spans.</param>
public class HighlightingColorizer(IHighlightingDefinition definition) : DocumentColorizingTransformer
{
    private DocumentHighlighter? _highlighter;
    private TextDocument? _highlighterDocument;

    internal IHighlightingDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    protected override void ColorizeLine(DocumentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (CurrentContext is not ITextRunConstructionContext context)
        {
            return;
        }

        // From the view per line, so a theme switch repaints in the other palette without rebuild.
        bool isDark = context.TextView.IsDarkTheme;
        if (Definition.MainRuleSet.Spans.Count > 0)
        {
            // Spans can cross lines, so the stateful highlighter owns the scan.
            foreach (var section in GetHighlighter(context.Document).HighlightLine(line.LineNumber).Sections)
            {
                ApplyColorToElement(line.Offset + section.Offset, section.Length, section.Color, isDark);
            }
            return;
        }

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

    /// <summary>Raised with the line range that must be repainted after a span crossed lines.</summary>
    public event HighlightingStateChangedEventHandler? HighlightingStateChanged;

    /// <summary>The highlighter for this document, built on first use and reused after.</summary>
    internal DocumentHighlighter GetHighlighter(TextDocument document)
    {
        if (_highlighter is null || !ReferenceEquals(_highlighterDocument, document))
        {
            _highlighter?.Dispose();
            _highlighter = new DocumentHighlighter(document, Definition);
            _highlighter.HighlightingStateChanged += (from, to) => HighlightingStateChanged?.Invoke(from, to);
            _highlighterDocument = document;
        }
        return _highlighter;
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
