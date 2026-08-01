using System.Text;

namespace Aprillz.MewUI.Text.Editing;

/// <summary>
/// Mutable text storage for editor consumers of the read-only text view contracts.
/// Mutation and undo are intentionally kept outside the layout engine.
/// </summary>
public sealed class EditableTextDocument : IReadOnlyTextDocument
{
    private readonly StringBuilder _text = new();
    private readonly List<EditableDocumentLine> _lines = [];

    public EditableTextDocument(string? text = null)
    {
        _text.Append(NormalizeNewLines(text ?? string.Empty));
        RebuildLines();
    }

    public int TextLength => _text.Length;
    public long Version { get; private set; }
    public int LineCount => _lines.Count;

    public event Action<TextChange>? Changed;

    public char GetCharAt(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset >= _text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        return _text[offset];
    }

    public string GetText(int offset, int length)
    {
        ValidateRange(offset, length);
        return _text.ToString(offset, length);
    }

    public override string ToString() => _text.ToString();

    public IReadOnlyDocumentLine GetLineByNumber(int lineNumber)
    {
        if ((uint)lineNumber >= (uint)_lines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }
        return _lines[lineNumber];
    }

    public IReadOnlyDocumentLine GetLineByOffset(int offset)
    {
        if (offset < 0 || offset > _text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        int low = 0;
        int high = _lines.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            var line = _lines[middle];
            if (offset < line.Offset)
            {
                high = middle - 1;
            }
            else if (middle + 1 < _lines.Count && offset >= _lines[middle + 1].Offset)
            {
                low = middle + 1;
            }
            else
            {
                return line;
            }
        }
        return _lines[^1];
    }

    public int GetOffset(int line, int column)
    {
        var documentLine = (EditableDocumentLine)GetLineByNumber(line);
        if (column < 0 || column > documentLine.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
        return documentLine.Offset + column;
    }

    public TextLocation GetLocation(int offset)
    {
        var line = (EditableDocumentLine)GetLineByOffset(offset);
        return new TextLocation(line.LineNumber, Math.Min(offset - line.Offset, line.Length));
    }

    public void SetText(string? text)
    {
        string normalized = NormalizeNewLines(text ?? string.Empty);
        string previous = _text.ToString();
        if (previous == normalized)
        {
            return;
        }

        _text.Clear();
        _text.Append(normalized);
        Version++;
        RebuildLines();
        Changed?.Invoke(new TextChange(0, previous.Length, normalized.Length));
    }

    public void Insert(int offset, string text) => Replace(offset, 0, text);

    public void Remove(int offset, int length) => Replace(offset, length, string.Empty);

    public void Replace(int offset, int length, string? text)
    {
        ValidateRange(offset, length);
        string normalized = NormalizeNewLines(text ?? string.Empty);
        if (length == 0 && normalized.Length == 0)
        {
            return;
        }
        if (length == normalized.Length &&
            string.Equals(_text.ToString(offset, length), normalized, StringComparison.Ordinal))
        {
            return;
        }

        _text.Remove(offset, length);
        _text.Insert(offset, normalized);
        Version++;
        RebuildLines();
        Changed?.Invoke(new TextChange(offset, length, normalized.Length));
    }

    private void RebuildLines()
    {
        _lines.Clear();
        int lineNumber = 0;
        int start = 0;
        for (int i = 0; i < _text.Length; i++)
        {
            if (_text[i] != '\n')
            {
                continue;
            }
            _lines.Add(new EditableDocumentLine(lineNumber++, start, i - start, 1));
            start = i + 1;
        }
        _lines.Add(new EditableDocumentLine(lineNumber, start, _text.Length - start, 0));
    }

    private void ValidateRange(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _text.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }

    internal static string NormalizeNewLines(string text)
        => text.IndexOf('\r') < 0
            ? text
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed class EditableDocumentLine(int lineNumber, int offset, int length, int delimiterLength)
        : IReadOnlyDocumentLine
    {
        public int LineNumber { get; } = lineNumber;
        public int Offset { get; } = offset;
        public int Length { get; } = length;
        public int TotalLength => Length + delimiterLength;
        public string Delimiter => delimiterLength == 0 ? string.Empty : "\n";
    }
}
