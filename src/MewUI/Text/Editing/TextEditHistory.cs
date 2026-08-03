namespace Aprillz.MewUI.Text.Editing;

/// <summary>
/// Undo/redo history owned by the document. Edits recorded through this class stay undoable;
/// any other document change is unrecorded and clears the history, so stale offsets can never
/// be replayed.
/// </summary>
internal sealed class TextEditHistory
{
    private readonly EditableTextDocument _document;
    private readonly Stack<EditCommand> _undo = new();
    private readonly Stack<EditCommand> _redo = new();
    private int _suppressDepth;

    internal TextEditHistory(EditableTextDocument document)
    {
        _document = document;
        _document.Changed += OnDocumentChanged;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Applies a replace and records it. Returns false without touching the document when the replacement is a no-op.</summary>
    public bool RecordReplace(
        int start,
        int removeLength,
        string inserted,
        int anchorBefore,
        int caretBefore,
        int anchorAfter,
        int caretAfter)
    {
        string removed = _document.GetText(start, removeLength);
        if (removed == inserted)
        {
            return false;
        }
        ReplaceSuppressed(start, removeLength, inserted);
        _undo.Push(new EditCommand(start, removed, inserted, anchorBefore, caretBefore, anchorAfter, caretAfter));
        _redo.Clear();
        return true;
    }

    /// <summary>Records an already-applied edit (composition commit aggregates its transient replaces).</summary>
    public void Push(
        int start,
        string removed,
        string inserted,
        int anchorBefore,
        int caretBefore,
        int anchorAfter,
        int caretAfter)
    {
        _undo.Push(new EditCommand(start, removed, inserted, anchorBefore, caretBefore, anchorAfter, caretAfter));
        _redo.Clear();
    }

    /// <summary>Applies a replace that neither records nor clears (composition intermediates).</summary>
    public void ReplaceTransient(int start, int length, string text)
        => ReplaceSuppressed(start, length, text);

    public bool TryUndo(out int anchor, out int caret)
    {
        if (!_undo.TryPop(out var command))
        {
            anchor = caret = 0;
            return false;
        }
        ReplaceSuppressed(command.Start, command.Inserted.Length, command.Removed);
        _redo.Push(command);
        anchor = command.AnchorBefore;
        caret = command.CaretBefore;
        return true;
    }

    public bool TryRedo(out int anchor, out int caret)
    {
        if (!_redo.TryPop(out var command))
        {
            anchor = caret = 0;
            return false;
        }
        ReplaceSuppressed(command.Start, command.Removed.Length, command.Inserted);
        _undo.Push(command);
        anchor = command.AnchorAfter;
        caret = command.CaretAfter;
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void ReplaceSuppressed(int start, int length, string text)
    {
        _suppressDepth++;
        try
        {
            _document.Replace(start, length, text);
        }
        finally
        {
            _suppressDepth--;
        }
    }

    private void OnDocumentChanged(TextChange change)
    {
        if (_suppressDepth == 0)
        {
            Clear();
        }
    }

    private readonly record struct EditCommand(
        int Start,
        string Removed,
        string Inserted,
        int AnchorBefore,
        int CaretBefore,
        int AnchorAfter,
        int CaretAfter);
}
