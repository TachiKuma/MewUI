using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

/// <summary>The code completion window.</summary>
public class CompletionWindow : CompletionWindowBase
{
    private const double DESCRIPTION_MAX_WIDTH = 320;
    private const double DESCRIPTION_MAX_HEIGHT = 300;
    private const double DESCRIPTION_GAP = 4;

    private readonly Border _descriptionFrame;
    private readonly Popup _descriptionPopup;

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
        // Filtering changes how many rows there are, and the window has to grow or shrink with them.
        CompletionList.VisibleItemsChanged += () => UpdatePosition();

        _descriptionFrame = new Border
        {
            Padding = new Thickness(6, 4, 6, 4),
            MaxWidth = DESCRIPTION_MAX_WIDTH,
            MaxHeight = DESCRIPTION_MAX_HEIGHT
        };
        _descriptionFrame.WithTheme(static (theme, frame) =>
        {
            frame.BorderThickness = theme.Metrics.ControlBorderThickness;
            frame.CornerRadius = theme.Metrics.ControlCornerRadius;
            frame.Background = theme.Palette.ContainerBackground;
            frame.BorderBrush = theme.Palette.ControlBorder;
        });
        // Not hit-testable, so it neither takes the mouse nor counts as an interactive popup that
        // would suppress tooltips; kept open explicitly, so only this window decides when it goes.
        _descriptionPopup = new Popup
        {
            Content = _descriptionFrame,
            StaysOpen = true,
            IsHitTestVisible = false
        };
        // The description follows the selection rather than the pointer: the list is walked with the
        // arrow keys, and a hover tooltip would never appear for that.
        CompletionList.SelectionChanged += (_, _) => UpdateDescription();
    }

    /// <summary>
    /// The items are added after the window is constructed, and nothing in the list observes that,
    /// so the rows are published here - which is also what gives the window its height.
    /// </summary>
    protected override void OnShowing()
    {
        base.OnShowing();
        CompletionList.ResetVisibleItems();
    }

    /// <summary>
    /// Puts the selected item's <see cref="ICompletionData.Description"/> beside the list, or takes
    /// the panel away when the item carries none. A string is shown wrapped; anything else is shown
    /// as it is, which is how the original lets a caller supply its own element.
    /// </summary>
    private void UpdateDescription()
    {
        if (CompletionList.SelectedItem is not ICompletionData item)
        {
            return;
        }

        if (item.Description is not object description)
        {
            _descriptionPopup.Close();
            return;
        }

        _descriptionFrame.Child = description is string text
            ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            : description as UIElement;
        if (_descriptionFrame.Child is null)
        {
            _descriptionPopup.Close();
            return;
        }

        PlaceDescription();
    }

    /// <inheritdoc/>
    protected override void OnPlaced()
    {
        base.OnPlaced();
        if (_descriptionPopup.IsOpen)
        {
            PlaceDescription();
        }
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _descriptionPopup.Close();
        base.OnClosed(e);
    }

    private void PlaceDescription()
    {
        if (!IsOpen || PlacedBounds.Width <= 0)
        {
            return;
        }

        // Level with the selected row rather than the top of the list, and clear of the window by a
        // gap: the anchor keeps the window's horizontal extent and takes the row's vertical one.
        var row = CompletionList.GetSelectedRowBounds();
        var anchor = new Rect(
            PlacedBounds.X,
            row.Height > 0 ? row.Y : PlacedBounds.Y,
            PlacedBounds.Width + DESCRIPTION_GAP,
            row.Height > 0 ? row.Height : PlacedBounds.Height);

        // Owned by the list frame, which is what the original anchors its tooltip to, so the panel
        // inherits the font the list already corrected away from the editor's monospace.
        DescriptionBounds = _descriptionPopup.IsOpen
            ? _descriptionPopup.MoveTo(anchor, PopupAnchorSide.Right)
            : _descriptionPopup.ShowAt(Root, anchor, PopupAnchorSide.Right);
    }

    /// <summary>The completion list used in this window.</summary>
    public CompletionList CompletionList { get; }

    /// <summary>The panel showing the selected item's description.</summary>
    internal Popup DescriptionPopup => _descriptionPopup;

    /// <summary>Where the description panel was last placed, in the owner window's coordinates.</summary>
    internal Rect DescriptionBounds { get; private set; }

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
