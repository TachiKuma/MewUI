using System.Text.RegularExpressions;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

/// <summary>
/// Scans one line at a time, carrying a span stack across lines so regions such as block comments
/// and verbatim strings keep their color past a line break.
/// </summary>
public sealed class HighlightingEngine(HighlightingRuleSet mainRuleSet)
{
    private static readonly HighlightingRuleSet EmptyRuleSet = new() { Name = "EmptyRuleSet" };

    private readonly HighlightingRuleSet _mainRuleSet = mainRuleSet ?? throw new ArgumentNullException(nameof(mainRuleSet));
    private readonly List<HighlightingSpan> _spanStack = [];
    private string _lineText = string.Empty;
    private int _position;
    private HighlightedLine? _line;

    /// <summary>Span stack the scanner starts the next line with.</summary>
    public IReadOnlyList<HighlightingSpan> SpanStack => _spanStack;

    public void SetSpanStack(IReadOnlyList<HighlightingSpan> stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _spanStack.Clear();
        _spanStack.AddRange(stack);
    }

    private HighlightingRuleSet CurrentRuleSet
        => _spanStack.Count == 0 ? _mainRuleSet : _spanStack[^1].RuleSet ?? EmptyRuleSet;

    /// <summary>Highlights one line and advances the span stack to the state that follows it.</summary>
    public HighlightedLine HighlightLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _lineText = text;
        _line = new HighlightedLine();
        ScanLine();
        var result = _line;
        _line = null;
        return result;
    }

    private void ScanLine()
    {
        _position = 0;
        while (true)
        {
            var ruleSet = CurrentRuleSet;
            Match? endMatch = _spanStack.Count > 0
                ? MatchOrNull(_spanStack[^1].EndExpression, _position)
                : null;
            Match? startMatch = null;
            HighlightingSpan? startSpan = null;
            foreach (var span in ruleSet.Spans)
            {
                var candidate = MatchOrNull(span.StartExpression, _position);
                if (candidate is not null && (startMatch is null || candidate.Index < startMatch.Index))
                {
                    startMatch = candidate;
                    startSpan = span;
                }
            }

            var first = Earlier(endMatch, startMatch);
            if (first is null)
            {
                break;
            }

            HighlightRules(first.Index);
            if (ReferenceEquals(first, endMatch))
            {
                var popped = _spanStack[^1];
                _spanStack.RemoveAt(_spanStack.Count - 1);
                AddSection(first.Index, first.Length, popped.EndColor
                    ?? (popped.SpanColorIncludesEnd ? popped.SpanColor : null));
            }
            else
            {
                AddSection(first.Index, first.Length, startSpan!.StartColor
                    ?? (startSpan.SpanColorIncludesStart ? startSpan.SpanColor : null));
                _spanStack.Add(startSpan);
            }
            _position = first.Index + first.Length;
        }

        HighlightRules(_lineText.Length);
    }

    /// <summary>Colors the run up to <paramref name="until"/> with the current rule set, or the open span's color.</summary>
    private void HighlightRules(int until)
    {
        if (_position >= until)
        {
            _position = Math.Max(_position, until);
            return;
        }

        if (_spanStack.Count > 0)
        {
            var span = _spanStack[^1];
            // The span colors the whole run first; its own rules then paint over what they match,
            // which is how a nested set (escapes inside a string, say) stays confined to the span.
            AddSection(_position, until - _position, span.SpanColor);
            ApplyRules(span.RuleSet?.Rules, until);
        }
        else
        {
            ApplyRules(CurrentRuleSet.Rules, until);
        }
        _position = until;
    }

    private void ApplyRules(IList<HighlightingRule>? rules, int until)
    {
        if (rules is null || rules.Count == 0)
        {
            return;
        }
        int scan = _position;
        while (scan < until)
        {
            Match? best = null;
            HighlightingRule? bestRule = null;
            foreach (var rule in rules)
            {
                var candidate = rule.Regex.Match(_lineText, scan, until - scan);
                if (!candidate.Success || candidate.Length == 0)
                {
                    continue;
                }
                if (best is null || candidate.Index < best.Index)
                {
                    best = candidate;
                    bestRule = rule;
                }
            }
            if (best is null)
            {
                break;
            }
            AddSection(best.Index, best.Length, bestRule!.Color);
            scan = best.Index + best.Length;
        }
    }

    private Match? MatchOrNull(Regex? expression, int start)
    {
        if (expression is null || start > _lineText.Length)
        {
            return null;
        }
        var match = expression.Match(_lineText, start);
        return match.Success ? match : null;
    }

    private static Match? Earlier(Match? left, Match? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return right.Index < left.Index ? right : left;
    }

    private void AddSection(int offset, int length, HighlightingColor? color)
    {
        if (color is null || length <= 0 || _line is null)
        {
            return;
        }
        _line.Sections.Add(new HighlightedSection(offset, length, color));
    }
}
