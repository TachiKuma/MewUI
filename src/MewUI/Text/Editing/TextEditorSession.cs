using System.Globalization;

namespace Aprillz.MewUI.Text.Editing;

/// <summary>Editing state layered over <see cref="EditableTextDocument"/>.</summary>
public sealed class TextEditorSession
{
    private readonly Stack<EditCommand> _undo = new();
    private readonly Stack<EditCommand> _redo = new();
    private CompositionState? _composition;
    private long _textElementVersion = -1;
    private int[] _textElementStarts = [];

    public TextEditorSession(EditableTextDocument document)
        => Document = document ?? throw new ArgumentNullException(nameof(document));

    public EditableTextDocument Document { get; }
    public int CaretPosition { get; private set; }
    public int AnchorPosition { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsComposing => _composition is not null;
    public TextRange Selection => new(
        Math.Min(CaretPosition, AnchorPosition),
        Math.Abs(CaretPosition - AnchorPosition));

    public event Action? StateChanged;

    public void SetCaret(int position, bool extendSelection = false)
    {
        position = Math.Clamp(position, 0, Document.TextLength);
        if (CaretPosition == position && (extendSelection || AnchorPosition == position))
        {
            return;
        }
        CaretPosition = position;
        if (!extendSelection)
        {
            AnchorPosition = position;
        }
        StateChanged?.Invoke();
    }

    public void SetSelection(int start, int length)
    {
        if (start < 0 || length < 0 || start > Document.TextLength - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        AnchorPosition = start;
        CaretPosition = start + length;
        StateChanged?.Invoke();
    }

    public void SelectAll()
    {
        AnchorPosition = 0;
        CaretPosition = Document.TextLength;
        StateChanged?.Invoke();
    }

    public void SelectWordAt(int position)
    {
        position = Math.Clamp(position, 0, Document.TextLength);
        if (Document.TextLength == 0)
        {
            SetCaret(0);
            return;
        }
        if (position == Document.TextLength) position--;
        char value = Document.GetCharAt(position);
        if (!IsWordCharacter(value))
        {
            int end = NextTextElement(position);
            SetSelection(position, Math.Max(1, end - position));
            return;
        }

        int start = position;
        int wordEnd = position + 1;
        while (start > 0 && IsWordCharacter(Document.GetCharAt(start - 1))) start--;
        while (wordEnd < Document.TextLength && IsWordCharacter(Document.GetCharAt(wordEnd))) wordEnd++;
        SetSelection(start, wordEnd - start);
    }

    public void MoveLogical(LogicalDirection direction, bool extendSelection, bool byWord = false)
    {
        if (!extendSelection && Selection.Length > 0)
        {
            SetCaret(direction == LogicalDirection.Backward ? Selection.Start : Selection.End);
            return;
        }

        int target = byWord
            ? direction == LogicalDirection.Backward
                ? FindPreviousWordBoundary(CaretPosition)
                : FindNextWordBoundary(CaretPosition)
            : direction == LogicalDirection.Backward
                ? PreviousTextElement(CaretPosition)
                : NextTextElement(CaretPosition);
        SetCaret(target, extendSelection);
    }

    public void ReplaceSelection(string? text)
    {
        string normalized = EditableTextDocument.NormalizeNewLines(text ?? string.Empty);
        var selection = Selection;
        ApplyAndRecord(selection.Start, selection.Length, normalized);
    }

    public void Backspace(bool byWord = false)
    {
        if (Selection.Length > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }
        int start = byWord ? FindPreviousWordBoundary(CaretPosition) : PreviousTextElement(CaretPosition);
        if (start < CaretPosition)
        {
            ApplyAndRecord(start, CaretPosition - start, string.Empty);
        }
    }

    public void Delete(bool byWord = false)
    {
        if (Selection.Length > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }
        int end = byWord ? FindNextWordBoundary(CaretPosition) : NextTextElement(CaretPosition);
        if (end > CaretPosition)
        {
            ApplyAndRecord(CaretPosition, end - CaretPosition, string.Empty);
        }
    }

    public void Undo()
    {
        CommitComposition();
        if (!_undo.TryPop(out var command))
        {
            return;
        }
        Document.Replace(command.Start, command.Inserted.Length, command.Removed);
        AnchorPosition = command.AnchorBefore;
        CaretPosition = command.CaretBefore;
        _redo.Push(command);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        CommitComposition();
        if (!_redo.TryPop(out var command))
        {
            return;
        }
        Document.Replace(command.Start, command.Removed.Length, command.Inserted);
        AnchorPosition = command.AnchorAfter;
        CaretPosition = command.CaretAfter;
        _undo.Push(command);
        StateChanged?.Invoke();
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void BeginComposition()
    {
        if (_composition is not null)
        {
            return;
        }
        var selection = Selection;
        _composition = new CompositionState(
            selection.Start,
            Document.GetText(selection.Start, selection.Length),
            AnchorPosition,
            CaretPosition);
        if (selection.Length > 0)
        {
            Document.Remove(selection.Start, selection.Length);
        }
        AnchorPosition = CaretPosition = selection.Start;
        StateChanged?.Invoke();
    }

    public void UpdateComposition(string? text)
    {
        BeginComposition();
        var state = _composition!;
        string normalized = EditableTextDocument.NormalizeNewLines(text ?? string.Empty);
        Document.Replace(state.Start, state.CurrentLength, normalized);
        state.CurrentLength = normalized.Length;
        AnchorPosition = CaretPosition = state.Start + normalized.Length;
        StateChanged?.Invoke();
    }

    public void CommitComposition()
    {
        if (_composition is not CompositionState state)
        {
            return;
        }
        string inserted = Document.GetText(state.Start, state.CurrentLength);
        _composition = null;
        if (state.Removed != inserted)
        {
            _undo.Push(new EditCommand(
                state.Start,
                state.Removed,
                inserted,
                state.AnchorBefore,
                state.CaretBefore,
                AnchorPosition,
                CaretPosition));
            _redo.Clear();
        }
        StateChanged?.Invoke();
    }

    public void CancelComposition()
    {
        if (_composition is not CompositionState state)
        {
            return;
        }
        Document.Replace(state.Start, state.CurrentLength, state.Removed);
        AnchorPosition = state.AnchorBefore;
        CaretPosition = state.CaretBefore;
        _composition = null;
        StateChanged?.Invoke();
    }

    private void ApplyAndRecord(int start, int removeLength, string inserted)
    {
        CommitComposition();
        string removed = Document.GetText(start, removeLength);
        if (removed == inserted)
        {
            SetCaret(start + inserted.Length);
            return;
        }

        int anchorBefore = AnchorPosition;
        int caretBefore = CaretPosition;
        Document.Replace(start, removeLength, inserted);
        AnchorPosition = CaretPosition = start + inserted.Length;
        _undo.Push(new EditCommand(
            start,
            removed,
            inserted,
            anchorBefore,
            caretBefore,
            AnchorPosition,
            CaretPosition));
        _redo.Clear();
        StateChanged?.Invoke();
    }

    private int PreviousTextElement(int position)
    {
        if (position <= 0)
        {
            return 0;
        }
        int[] starts = GetTextElementStarts();
        int index = Array.BinarySearch(starts, position);
        if (index >= 0)
        {
            return index == 0 ? 0 : starts[index - 1];
        }
        index = ~index;
        return index == 0 ? 0 : starts[index - 1];
    }

    private int NextTextElement(int position)
    {
        if (position >= Document.TextLength)
        {
            return Document.TextLength;
        }
        int[] starts = GetTextElementStarts();
        int index = Array.BinarySearch(starts, position);
        index = index >= 0 ? index + 1 : ~index;
        return index < starts.Length ? starts[index] : Document.TextLength;
    }

    private int FindPreviousWordBoundary(int position)
    {
        int current = position;
        while (current > 0 && char.IsWhiteSpace(Document.GetCharAt(current - 1)))
        {
            current--;
        }
        while (current > 0 && IsWordCharacter(Document.GetCharAt(current - 1)))
        {
            current--;
        }
        return current;
    }

    private int[] GetTextElementStarts()
    {
        if (_textElementVersion == Document.Version)
        {
            return _textElementStarts;
        }

        _textElementStarts = StringInfo.ParseCombiningCharacters(Document.ToString());
        _textElementVersion = Document.Version;
        return _textElementStarts;
    }

    private int FindNextWordBoundary(int position)
    {
        int current = position;
        while (current < Document.TextLength && IsWordCharacter(Document.GetCharAt(current)))
        {
            current++;
        }
        while (current < Document.TextLength && char.IsWhiteSpace(Document.GetCharAt(current)))
        {
            current++;
        }
        return current;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private readonly record struct EditCommand(
        int Start,
        string Removed,
        string Inserted,
        int AnchorBefore,
        int CaretBefore,
        int AnchorAfter,
        int CaretAfter);

    private sealed class CompositionState(int start, string removed, int anchorBefore, int caretBefore)
    {
        public int Start { get; } = start;
        public string Removed { get; } = removed;
        public int AnchorBefore { get; } = anchorBefore;
        public int CaretBefore { get; } = caretBefore;
        public int CurrentLength { get; set; }
    }
}
