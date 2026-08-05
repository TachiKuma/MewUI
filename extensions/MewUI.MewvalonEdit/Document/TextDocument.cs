using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Document;

public sealed class TextDocument : ITextSource
{
    private readonly EditableTextDocument _document;
    private readonly List<WeakReference<TextAnchor>> _anchors = [];
    private TextSourceVersion _version;

    public TextDocument()
        : this(string.Empty)
    {
    }

    public TextDocument(string? text)
    {
        // Terminators are kept so a file read into the editor comes back out unchanged.
        _document = EditableTextDocument.CreatePreservingLineEndings(text);
        _version = new TextSourceVersion(this, 0);
        _document.Changed += OnChanged;
    }

    public TextDocument(IEnumerable<char> initialText)
        : this(initialText is null ? null : string.Concat(initialText))
    {
    }

    internal TextDocument(EditableTextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _version = new TextSourceVersion(this, 0);
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

    /// <summary>
    /// Checkpoint of the current text. Hold one to carry offsets across later edits; the value the
    /// engine uses to invalidate caches is <see cref="CoreDocument"/>'s own counter.
    /// </summary>
    public ITextSourceVersion Version => _version;

    /// <summary>An unchanging copy of the whole text.</summary>
    public ITextSource CreateSnapshot() => new StringTextSource(Text);

    /// <summary>An unchanging copy of one range.</summary>
    public ITextSource CreateSnapshot(int offset, int length) => new StringTextSource(GetText(offset, length));

    public event EventHandler<DocumentChangeEventArgs>? Changed;
    public event EventHandler? TextChanged;

    public char GetCharAt(int offset) => _document.GetCharAt(offset);
    public string GetText(int offset, int length) => _document.GetText(offset, length);

    public string GetText(ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return GetText(segment.Offset, segment.Length);
    }

    /// <summary>
    /// Surface this document is shown in, once an editor adopts it. Edits route through it so they
    /// join the undo history; editing the core document directly is unrecorded and drops that history.
    /// </summary>
    internal MultiLineTextBox? Surface { get; set; }

    public void Insert(int offset, string text) => Replace(offset, 0, text);
    public void Remove(int offset, int length) => Replace(offset, length, string.Empty);

    public void Replace(int offset, int length, string? text)
    {
        if (Surface is MultiLineTextBox surface)
        {
            surface.ReplaceRange(offset, length, text);
        }
        else
        {
            _document.Replace(offset, length, text);
        }
    }
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

    /// <summary>
    /// An anchor at <paramref name="offset"/> that moves with the text around it. Anchors are held
    /// weakly, so one nobody keeps a reference to costs the document nothing.
    /// </summary>
    public TextAnchor CreateAnchor(int offset)
    {
        if (offset < 0 || offset > TextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        var anchor = new TextAnchor(this, offset);
        _anchors.Add(new WeakReference<TextAnchor>(anchor));
        return anchor;
    }

    private void UpdateAnchors(in OffsetChangeMapEntry change)
    {
        for (int index = _anchors.Count - 1; index >= 0; index--)
        {
            if (_anchors[index].TryGetTarget(out var anchor))
            {
                anchor.Update(in change);
            }
            else
            {
                _anchors.RemoveAt(index);
            }
        }
    }

    private void OnChanged(TextChange change)
    {
        var entry = new OffsetChangeMapEntry(change.Offset, change.RemovedLength, change.InsertedLength);
        UpdateAnchors(in entry);
        _version = _version.Append(entry);
        Changed?.Invoke(this, new DocumentChangeEventArgs(change.Offset, change.RemovedLength, change.InsertedLength));
        TextChanged?.Invoke(this, EventArgs.Empty);
    }
}
