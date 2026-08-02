using System.Text;

namespace Aprillz.MewUI.Text.Editing;

/// <summary>
/// Mutable text storage for editor consumers of the read-only text view contracts.
/// Mutation and undo are intentionally kept outside the layout engine.
/// </summary>
public sealed class EditableTextDocument : IReadOnlyTextDocument
{
    private readonly StringBuilder _text = new();
    private readonly EditableLineIndex _lines = new();

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
        var line = _lines.GetByNumber(lineNumber);
        return new EditableDocumentLine(
            line.LineNumber,
            line.Offset,
            line.Length,
            line.DelimiterLength);
    }

    public IReadOnlyDocumentLine GetLineByOffset(int offset)
    {
        if (offset < 0 || offset > _text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var line = _lines.GetByOffset(offset, _text.Length);
        return new EditableDocumentLine(
            line.LineNumber,
            line.Offset,
            line.Length,
            line.DelimiterLength);
    }

    public int GetOffset(int line, int column)
    {
        var documentLine = _lines.GetByNumber(line);
        if (column < 0 || column > documentLine.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
        return documentLine.Offset + column;
    }

    public TextLocation GetLocation(int offset)
    {
        var line = _lines.GetByOffset(offset, _text.Length);
        return new TextLocation(line.LineNumber, Math.Min(offset - line.Offset, line.Length));
    }

    public void SetText(string? text)
    {
        string normalized = NormalizeNewLines(text ?? string.Empty);
        if (ContentEquals(normalized))
        {
            return;
        }

        int previousLength = _text.Length;
        _text.Clear();
        _text.Append(normalized);
        Version++;
        RebuildLines();
        Changed?.Invoke(new TextChange(0, previousLength, normalized.Length));
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
        if (length == normalized.Length && RangeEquals(offset, normalized))
        {
            return;
        }

        var startLine = _lines.GetByOffset(offset, _text.Length);
        var endLine = _lines.GetByOffset(offset + length, _text.Length);
        bool changesLineStructure = normalized.Contains('\n') ||
            offset + length > startLine.Offset + startLine.Length;
        int affectedStart = startLine.Offset;
        int affectedEnd = endLine.Offset + endLine.Length + endLine.DelimiterLength;
        bool hasFollowingLine = endLine.LineNumber + 1 < _lines.Count;

        _text.Remove(offset, length);
        _text.Insert(offset, normalized);
        if (!changesLineStructure)
        {
            _lines.SetLineLength(startLine.LineNumber, startLine.Length - length + normalized.Length);
        }
        else
        {
            int affectedLength = affectedEnd - affectedStart - length + normalized.Length;
            string affectedText = _text.ToString(affectedStart, affectedLength);
            var replacement = ParseLines(affectedText, includeFinalLine: !hasFollowingLine);
            _lines.ReplaceRange(
                startLine.LineNumber,
                endLine.LineNumber - startLine.LineNumber + 1,
                replacement);
        }
        Version++;
        Changed?.Invoke(new TextChange(offset, length, normalized.Length));
    }

    private void RebuildLines()
    {
        var lines = new List<EditableLineRecord>();
        int start = 0;
        for (int index = 0; index < _text.Length; index++)
        {
            if (_text[index] != '\n')
            {
                continue;
            }
            lines.Add(new EditableLineRecord(index - start, 1));
            start = index + 1;
        }
        lines.Add(new EditableLineRecord(_text.Length - start, 0));
        _lines.Reset(lines);
    }

    private static List<EditableLineRecord> ParseLines(string text, bool includeFinalLine)
    {
        var lines = new List<EditableLineRecord>();
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }
            lines.Add(new EditableLineRecord(index - start, 1));
            start = index + 1;
        }
        if (includeFinalLine || start < text.Length)
        {
            lines.Add(new EditableLineRecord(text.Length - start, 0));
        }
        if (lines.Count == 0)
        {
            lines.Add(new EditableLineRecord(0, 0));
        }
        return lines;
    }

    private void ValidateRange(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _text.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }

    private bool ContentEquals(string value)
    {
        if (_text.Length != value.Length)
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            if (_text[index] != value[index])
            {
                return false;
            }
        }

        return true;
    }

    private bool RangeEquals(int offset, string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (_text[offset + index] != value[index])
            {
                return false;
            }
        }
        return true;
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
