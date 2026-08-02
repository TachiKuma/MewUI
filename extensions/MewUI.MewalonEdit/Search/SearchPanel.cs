using System.Text.RegularExpressions;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Search;

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

    private SearchPanel(TextEditor editor)
    {
        _editor = editor;
        _document = editor.Document;
        _editor.Surface.Extensions.Classifiers.Add(this);
        _document.TextChanged += OnDocumentTextChanged;
        _editor.SurfaceChanged += OnSurfaceChanged;
    }

    public static SearchPanel Install(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return new SearchPanel(editor);
    }

    public static SearchPanel Install(ICSharpCode.AvalonEdit.Editing.TextArea textArea)
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
        panel._document.TextChanged -= panel.OnDocumentTextChanged;
        panel._editor.SurfaceChanged -= panel.OnSurfaceChanged;
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

    public Color MarkerColor { get; set; } = Color.FromArgb(150, 255, 215, 0);
    public IReadOnlyList<SearchResult> Results => _results;

    public SearchResult? FindNext(int startOffset = -1)
    {
        if (_results.Count == 0) return null;
        if (startOffset < 0) startOffset = _editor.SelectionStart + _editor.SelectionLength;
        var result = _results.FirstOrDefault(item => item.Offset >= startOffset);
        if (result.Length == 0) result = _results[0];
        _editor.Select(result.Offset, result.Length);
        return result;
    }

    public int ReplaceAll(string? replacement)
    {
        replacement ??= string.Empty;
        int count = _results.Count;
        for (int index = _results.Count - 1; index >= 0; index--)
        {
            var result = _results[index];
            _editor.Document.Replace(result.Offset, result.Length, replacement);
        }
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

        string pattern = UseRegex ? _searchPattern : Regex.Escape(_searchPattern);
        if (WholeWords) pattern = $@"\b(?:{pattern})\b";
        RegexOptions options = RegexOptions.CultureInvariant;
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
        _editor.InvalidateTextView();
    }

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        int lineStart = context.LogicalLine.Offset;
        int lineEnd = lineStart + context.LogicalLine.Length;
        foreach (var result in _results)
        {
            int start = Math.Max(lineStart, result.Offset);
            int end = Math.Min(lineEnd, result.EndOffset);
            if (end > start)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Background: MarkerColor));
            }
        }
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e) => Refresh();

    private void OnSurfaceChanged(NewMultiLineTextBox previous, NewMultiLineTextBox current)
    {
        previous.Extensions.Classifiers.Remove(this);
        _document.TextChanged -= OnDocumentTextChanged;
        _document = _editor.Document;
        _document.TextChanged += OnDocumentTextChanged;
        current.Extensions.Classifiers.Add(this);
        Refresh();
    }
}
