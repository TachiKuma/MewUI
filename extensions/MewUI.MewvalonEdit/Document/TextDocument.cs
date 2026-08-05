using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Document;

public sealed class TextDocument : ITextSource
{
    private readonly EditableTextDocument _document;

    public TextDocument()
        : this(string.Empty)
    {
    }

    public TextDocument(string? text)
    {
        _document = new EditableTextDocument(text);
        _document.Changed += OnChanged;
    }

    public TextDocument(IEnumerable<char> initialText)
        : this(initialText is null ? null : string.Concat(initialText))
    {
    }

    internal TextDocument(EditableTextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Changed += OnChanged;
    }

    internal EditableTextDocument CoreDocument => _document;

    public string Text
    {
        get => _document.ToString();
        set => _document.SetText(value);
    }

    public int TextLength => _document.TextLength;
    public int LineCount => _document.LineCount;
    public long Version => _document.Version;

    public event EventHandler<DocumentChangeEventArgs>? Changed;
    public event EventHandler? TextChanged;

    public char GetCharAt(int offset) => _document.GetCharAt(offset);
    public string GetText(int offset, int length) => _document.GetText(offset, length);

    public string GetText(ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return GetText(segment.Offset, segment.Length);
    }

    public void Insert(int offset, string text) => _document.Insert(offset, text);
    public void Remove(int offset, int length) => _document.Remove(offset, length);
    public void Replace(int offset, int length, string? text) => _document.Replace(offset, length, text);
    public void Replace(ISegment segment, string? text)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Replace(segment.Offset, segment.Length, text);
    }

    public DocumentLine GetLineByNumber(int lineNumber)
    {
        if (lineNumber <= 0) throw new ArgumentOutOfRangeException(nameof(lineNumber));
        return new DocumentLine(_document.GetLineByNumber(lineNumber - 1));
    }

    public DocumentLine GetLineByOffset(int offset)
        => new(_document.GetLineByOffset(offset));

    public int GetOffset(int line, int column)
    {
        if (line <= 0) throw new ArgumentOutOfRangeException(nameof(line));
        if (column <= 0) throw new ArgumentOutOfRangeException(nameof(column));
        return _document.GetOffset(line - 1, column - 1);
    }

    public TextLocation GetLocation(int offset)
    {
        var location = _document.GetLocation(offset);
        return new TextLocation(location.Line + 1, location.Column + 1);
    }

    /// <summary>The document's lines. Read-only, as in the original; mutation throws.</summary>
    public IList<DocumentLine> Lines
        => Enumerable.Range(0, _document.LineCount)
            .Select(index => new DocumentLine(_document.GetLineByNumber(index)))
            .ToArray();

    public override string ToString() => Text;

    private void OnChanged(TextChange change)
    {
        Changed?.Invoke(this, new DocumentChangeEventArgs(change.Offset, change.RemovedLength, change.InsertedLength));
        TextChanged?.Invoke(this, EventArgs.Empty);
    }
}
