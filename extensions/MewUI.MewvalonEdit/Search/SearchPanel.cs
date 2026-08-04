using System.Text.RegularExpressions;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Search;

public sealed class SearchPanel : ITextClassifier
{
    private readonly TextEditor _editor;
    private readonly List<SearchResult> _results = [];
    private TextDocument _document;
    private string _searchPattern = string.Empty;
    private bool _matchCase;
    private bool _useRegex;
    private bool _wholeWords;
    private bool _uninstalled;
    private bool _suspendDocumentRefresh;

    private SearchPanel(TextEditor editor)
    {
        _editor = editor;
        _document = editor.Document;
        _editor.Surface.Extensions.Classifiers.Add(this);
        _document.Changed += OnDocumentChanged;
        _editor.DocumentChanged += OnEditorDocumentChanged;
    }

    public static SearchPanel Install(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new SearchPanel(editor);
    }

    public static SearchPanel Install(Aprillz.MewUI.MewvalonEdit.Editing.TextArea textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        return Install(textArea.Editor);
    }

    public static void Uninstall(SearchPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel._uninstalled) return;
        panel._uninstalled = true;
        panel._editor.Surface.Extensions.Classifiers.Remove(panel);
        panel._document.Changed -= panel.OnDocumentChanged;
        panel._editor.DocumentChanged -= panel.OnEditorDocumentChanged;
        panel._editor.InvalidateTextView();
    }

    public string SearchPattern
    {
        get => _searchPattern;
        set
        {
            value ??= string.Empty;
            if (_searchPattern == value) return;
            _searchPattern = value;
            Refresh();
        }
    }

    public bool MatchCase
    {
        get => _matchCase;
        set { if (_matchCase != value) { _matchCase = value; Refresh(); } }
    }

    public bool UseRegex
    {
        get => _useRegex;
        set { if (_useRegex != value) { _useRegex = value; Refresh(); } }
    }

    public bool WholeWords
    {
        get => _wholeWords;
        set { if (_wholeWords != value) { _wholeWords = value; Refresh(); } }
    }

    public Color MarkerBrush { get; set; } = Color.FromArgb(150, 255, 215, 0);
    public IReadOnlyList<SearchResult> Results => _results;

    public SearchResult? FindNext(int startOffset = -1)
    {
        if (_results.Count == 0) return null;
        if (startOffset < 0) startOffset = _editor.SelectionStart + _editor.SelectionLength;
        int index = LowerBoundByOffset(startOffset);
        var result = index < _results.Count ? _results[index] : _results[0];
        _editor.Select(result.Offset, result.Length);
        return result;
    }

    public int ReplaceAll(string? replacement)
    {
        replacement ??= string.Empty;
        int count = _results.Count;
        _suspendDocumentRefresh = true;
        try
        {
            for (int index = _results.Count - 1; index >= 0; index--)
            {
                var result = _results[index];
                _editor.Document.Replace(result.Offset, result.Length, replacement);
            }
        }
        finally
        {
            _suspendDocumentRefresh = false;
        }
        Refresh();
        return count;
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_uninstalled, this);
        _results.Clear();
        if (string.IsNullOrEmpty(_searchPattern))
        {
            _editor.InvalidateTextView();
            return;
        }

        if (!UseRegex)
        {
            FindPlainTextMatches(0, _editor.Document.TextLength, _results);
            _editor.InvalidateTextView();
            return;
        }

        string pattern = WholeWords ? $@"\b(?:{_searchPattern})\b" : _searchPattern;
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
        if (!MatchCase) options |= RegexOptions.IgnoreCase;
        try
        {
            var regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(100));
            foreach (Match match in regex.Matches(_editor.Document.Text))
            {
                if (match.Success && match.Length > 0)
                    _results.Add(new SearchResult(match.Index, match.Length));
            }
        }
        catch (ArgumentException)
        {
            // An incomplete interactive regular expression has no results until it becomes valid.
        }
        catch (RegexMatchTimeoutException)
        {
            // An expensive interactive expression yields no results instead of blocking input.
            _results.Clear();
        }
        _editor.InvalidateTextView();
    }

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        int lineStart = context.LogicalLine.Offset;
        int lineEnd = lineStart + context.LogicalLine.Length;
        int index = LowerBoundByEnd(lineStart);
        for (; index < _results.Count; index++)
        {
            var result = _results[index];
            if (result.Offset >= lineEnd) break;
            int start = Math.Max(lineStart, result.Offset);
            int end = Math.Min(lineEnd, result.EndOffset);
            if (end > start)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Background: MarkerBrush));
            }
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_suspendDocumentRefresh) return;
        if (UseRegex || string.IsNullOrEmpty(_searchPattern))
        {
            Refresh();
            return;
        }

        int delta = e.InsertionLength - e.RemovalLength;
        int scanPadding = _searchPattern.Length + (WholeWords ? 1 : 0);
        int scanStart = Math.Max(0, e.Offset - scanPadding);
        int scanEnd = Math.Min(_editor.Document.TextLength, e.Offset + e.InsertionLength + scanPadding);
        var retained = new List<SearchResult>(_results.Count);
        foreach (var result in _results)
        {
            SearchResult adjusted;
            if (result.EndOffset <= e.Offset)
            {
                adjusted = result;
            }
            else if (result.Offset >= e.Offset + e.RemovalLength)
            {
                adjusted = result with { Offset = result.Offset + delta };
            }
            else
            {
                continue;
            }

            if (adjusted.EndOffset <= scanStart || adjusted.Offset >= scanEnd)
            {
                retained.Add(adjusted);
            }
        }

        FindPlainTextMatches(scanStart, scanEnd, retained);
        retained.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        _results.Clear();
        _results.AddRange(retained);
        _editor.InvalidateTextView();
    }

    private void FindPlainTextMatches(int scanStart, int scanEnd, List<SearchResult> output)
    {
        const int BlockSize = 64 * 1024;
        int patternLength = _searchPattern.Length;
        int documentLength = _editor.Document.TextLength;
        int lastStart = Math.Min(documentLength - patternLength, scanEnd);
        int blockStart = Math.Max(0, scanStart);
        while (blockStart <= lastStart)
        {
            int primaryLength = Math.Min(BlockSize, lastStart - blockStart + 1);
            int textLength = Math.Min(documentLength - blockStart, primaryLength + patternLength - 1);
            string block = _editor.Document.GetText(blockStart, textLength);
            for (int localOffset = 0; localOffset < primaryLength; localOffset++)
            {
                int offset = blockStart + localOffset;
                if (!MatchesAt(block, localOffset)) continue;
                if (WholeWords &&
                    ((offset > 0 && IsWordCharacter(_editor.Document.GetCharAt(offset - 1))) ||
                     (offset + patternLength < documentLength &&
                      IsWordCharacter(_editor.Document.GetCharAt(offset + patternLength)))))
                {
                    continue;
                }
                output.Add(new SearchResult(offset, patternLength));
                localOffset += Math.Max(0, patternLength - 1);
            }
            blockStart += primaryLength;
        }
    }

    private bool MatchesAt(string block, int offset)
    {
        for (int index = 0; index < _searchPattern.Length; index++)
        {
            char actual = block[offset + index];
            char expected = _searchPattern[index];
            if (MatchCase ? actual != expected : char.ToUpperInvariant(actual) != char.ToUpperInvariant(expected))
            {
                return false;
            }
        }
        return true;
    }

    private int LowerBoundByOffset(int offset)
    {
        int low = 0;
        int high = _results.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_results[middle].Offset < offset) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private int LowerBoundByEnd(int offset)
    {
        int low = 0;
        int high = _results.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_results[middle].EndOffset <= offset) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        _document.Changed -= OnDocumentChanged;
        _document = _editor.Document;
        _document.Changed += OnDocumentChanged;
        Refresh();
    }
}
