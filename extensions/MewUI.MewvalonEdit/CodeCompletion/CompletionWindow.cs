using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

/// <summary>The code completion window.</summary>
public class CompletionWindow : CompletionWindowBase
{
    public CompletionWindow(TextArea textArea) : base(textArea)
    {
        CompletionList = new CompletionList();
        Root.Child = CompletionList.Root;
        // The original's shape: fixed width, automatic height up to a cap. The frame is the
        // list's own, as in the original.
        Root.Width = 175;
        Root.MaxHeight = 300;
        Root.MinHeight = 15;
        Root.MinWidth = 30;
    }

    /// <summary>The completion list used in this window.</summary>
    public CompletionList CompletionList { get; }

    /// <summary>
    /// Whether the window closes automatically: on focus loss and when the caret leaves the
    /// completion region. The default is true.
    /// </summary>
    public bool CloseAutomatically { get; set; } = true;

    /// <summary>
    /// When set, the window also closes when the caret reaches the beginning of the allowed
    /// range. Useful for Ctrl+Space and complete-when-typing, but not for dot-completion. Has no
    /// effect when <see cref="CloseAutomatically"/> is false.
    /// </summary>
    public bool CloseWhenCaretAtBeginning { get; set; }

    protected override void AttachEvents()
    {
        base.AttachEvents();
        CompletionList.InsertionRequested += OnInsertionRequested;
        TextArea.Caret.PositionChanged += OnCaretPositionChanged;
    }

    protected override void DetachEvents()
    {
        CompletionList.InsertionRequested -= OnInsertionRequested;
        TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        base.DetachEvents();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!e.Handled)
        {
            CompletionList.HandleKey(e);
        }
    }

    private void OnInsertionRequested(object? sender, EventArgs e)
    {
        // The window must close before Complete() is called: if the callback pushes stacked input
        // handlers (a snippet does), closing afterwards would pop them again.
        int startOffset = StartOffset;
        int endOffset = EndOffset;
        var item = CompletionList.SelectedItem;
        Close();
        item?.Complete(TextArea, new SimpleSegment(startOffset, endOffset - startOffset), e);
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }
        int offset = TextArea.Caret.Offset;
        if (offset == StartOffset)
        {
            if (CloseAutomatically && CloseWhenCaretAtBeginning)
            {
                Close();
            }
            else
            {
                CompletionList.SelectItem(string.Empty);
            }
            return;
        }
        if (offset < StartOffset || offset > EndOffset)
        {
            if (CloseAutomatically)
            {
                Close();
            }
        }
        else
        {
            CompletionList.SelectItem(TextArea.Document.GetText(StartOffset, offset - StartOffset));
        }
    }
}
