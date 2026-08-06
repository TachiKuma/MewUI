using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Document;

/// <summary>
/// The document's undo and redo history. Grouping is what it is mostly used for: a routine that
/// edits line by line, such as indenting a block, would otherwise cost one undo step per line.
/// </summary>
/// <remarks>
/// The original raises <c>PropertyChanged</c> for <see cref="CanUndo"/> and <see cref="CanRedo"/>.
/// These read the document instead, because the core raises no signal once the history has changed:
/// its own change event runs while the edit is still being recorded. A binding over them therefore
/// has nothing to follow yet.
/// </remarks>
public sealed class UndoStack
{
    private readonly EditableTextDocument _document;
    private IDisposable? _group;
    private int _groupDepth;

    internal UndoStack(TextDocument document) => _document = document.CoreDocument;

    /// <summary>Whether there is an edit to undo.</summary>
    public bool CanUndo => _document.CanUndo;

    /// <summary>Whether there is an undone edit to redo.</summary>
    public bool CanRedo => _document.CanRedo;

    /// <summary>
    /// Most recent edits to keep, oldest dropped past it. Negative keeps every edit; zero records
    /// none. A group counts as the edits it holds, and the limit never leaves half of one behind.
    /// </summary>
    public int SizeLimit
    {
        get => _document.UndoSizeLimit;
        set => _document.UndoSizeLimit = value;
    }

    /// <summary>Whether a group is open, which is when an edit joins the one before it.</summary>
    public bool IsInUndoGroup => _groupDepth > 0;

    /// <summary>
    /// Opens a group; every edit until the matching <see cref="EndUndoGroup"/> undoes as one step.
    /// Nesting extends the outermost group rather than starting another.
    /// </summary>
    public void StartUndoGroup()
    {
        if (_groupDepth++ == 0)
        {
            _group = _document.BeginUndoGroup();
        }
    }

    /// <summary>Closes the group opened by <see cref="StartUndoGroup"/>.</summary>
    /// <exception cref="InvalidOperationException">No group is open.</exception>
    public void EndUndoGroup()
    {
        if (_groupDepth == 0)
        {
            throw new InvalidOperationException("No undo group is open.");
        }
        if (--_groupDepth == 0)
        {
            _group?.Dispose();
            _group = null;
        }
    }

    /// <summary>
    /// A group that closes when the returned object is disposed, which is the shape a caller wants
    /// when the edits are made inside one scope.
    /// </summary>
    public IDisposable OpenUndoGroup()
    {
        StartUndoGroup();
        return new GroupScope(this);
    }

    /// <summary>Undoes one step. False when there was nothing to undo.</summary>
    public bool Undo() => _document.Undo();

    /// <summary>Redoes one step. False when there was nothing to redo.</summary>
    public bool Redo() => _document.Redo();

    /// <summary>Drops both stacks, so what is in the document becomes the starting point.</summary>
    public void ClearAll() => _document.ClearUndoHistory();

    private sealed class GroupScope(UndoStack stack) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended)
            {
                return;
            }
            _ended = true;
            stack.EndUndoGroup();
        }
    }
}
