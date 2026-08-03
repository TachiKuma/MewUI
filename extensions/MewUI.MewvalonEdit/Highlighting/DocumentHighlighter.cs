using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Highlighting;

/// <summary>Highlights a document line by line, caching the span stack each line ends with.</summary>
public interface IHighlighter
{
    IHighlightingDefinition Definition { get; }

    HighlightedLine HighlightLine(int lineNumber);
}

/// <summary>
/// Stateful highlighter over a whole document. Line states are cached, so a line is only rescanned
/// when the text before it changed the span stack it starts from.
/// </summary>
public sealed class DocumentHighlighter : IHighlighter, IDisposable
{
    private readonly TextDocument _document;
    private readonly HighlightingEngine _engine;

    // Span stack each line ends with. Index i holds the state after line i+1, so line n starts
    // from _endStates[n - 2]. Entries past _validUpTo are stale.
    private readonly List<IReadOnlyList<HighlightingSpan>> _endStates = [];
    private int _validUpTo;

    public DocumentHighlighter(TextDocument document, IHighlightingDefinition definition)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _engine = new HighlightingEngine(definition.MainRuleSet);
        _document.Changed += OnDocumentChanged;
    }

    public IHighlightingDefinition Definition { get; }

    /// <summary>Raised with the line range whose highlighting changed because a span crossed lines.</summary>
    public event Action<int, int>? HighlightingStateChanged;

    public HighlightedLine HighlightLine(int lineNumber)
    {
        if (lineNumber <= 0 || lineNumber > _document.LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        ScanUpTo(lineNumber - 1);
        _engine.SetSpanStack(lineNumber == 1 ? [] : _endStates[lineNumber - 2]);
        var line = _document.GetLineByNumber(lineNumber);
        var result = _engine.HighlightLine(_document.GetText(line.Offset, line.Length));
        StoreEndState(lineNumber, _engine.SpanStack);
        return result;
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;

    /// <summary>Scans forward until the end state of <paramref name="lineNumber"/> is known.</summary>
    private void ScanUpTo(int lineNumber)
    {
        for (int current = _validUpTo + 1; current <= lineNumber; current++)
        {
            _engine.SetSpanStack(current == 1 ? [] : _endStates[current - 2]);
            var line = _document.GetLineByNumber(current);
            _engine.HighlightLine(_document.GetText(line.Offset, line.Length));
            StoreEndState(current, _engine.SpanStack);
        }
    }

    private void StoreEndState(int lineNumber, IReadOnlyList<HighlightingSpan> stack)
    {
        var snapshot = stack.Count == 0 ? Array.Empty<HighlightingSpan>() : stack.ToArray();
        IReadOnlyList<HighlightingSpan>? previous = _endStates.Count >= lineNumber
            ? _endStates[lineNumber - 1]
            : null;
        while (_endStates.Count < lineNumber)
        {
            _endStates.Add(Array.Empty<HighlightingSpan>());
        }
        _endStates[lineNumber - 1] = snapshot;
        _validUpTo = Math.Max(_validUpTo, lineNumber);

        // The following lines start from a different stack than the cached scan assumed.
        if (previous is not null && !SameStack(previous, snapshot) && lineNumber < _document.LineCount)
        {
            HighlightingStateChanged?.Invoke(lineNumber + 1, _document.LineCount);
        }
    }

    private static bool SameStack(IReadOnlyList<HighlightingSpan> left, IReadOnlyList<HighlightingSpan> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!ReferenceEquals(left[index], right[index])) return false;
        }
        return true;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        int firstAffected = _document.GetLocation(Math.Min(e.Offset, _document.TextLength)).Line;
        _validUpTo = Math.Min(_validUpTo, Math.Max(0, firstAffected - 1));

        // Stale entries past _validUpTo are kept so the next scan can tell whether the state that
        // the following lines were highlighted with actually changed. Only vanished lines are cut.
        if (_endStates.Count > _document.LineCount)
        {
            _endStates.RemoveRange(_document.LineCount, _endStates.Count - _document.LineCount);
        }
    }
}
