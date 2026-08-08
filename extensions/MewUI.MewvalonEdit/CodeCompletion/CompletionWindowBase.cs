using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

/// <summary>
/// Base class for completion windows. The window is a panel on the editor's overlay layer rather
/// than an OS window: the keyboard focus stays in the editor, and a stacked input handler feeds
/// the keys to the window - which is also what closes it when any other input handler takes over.
/// It anchors to <see cref="StartOffset"/> and follows it across document changes and scrolling.
/// </summary>
public class CompletionWindowBase
{
    private readonly InputHandler _inputHandler;
    private TextDocument _document;
    private bool _isOpen;

    public CompletionWindowBase(TextArea textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        TextArea = textArea;
        _document = textArea.Document;
        StartOffset = EndOffset = textArea.Caret.Offset;
        _inputHandler = new InputHandler(this);
        Root = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = 1
        };
        Root.WithTheme(static (theme, root) =>
        {
            root.CornerRadius = theme.Metrics.ControlCornerRadius;
            root.Background = theme.Palette.ContainerBackground;
            root.BorderBrush = theme.Palette.ControlBorder;
            // The panel hangs inside the editor, whose monospace font would otherwise inherit.
            root.FontFamily(theme.Metrics.FontFamily).FontSize(theme.Metrics.FontSize);
        });
    }

    /// <summary>The text area the window belongs to.</summary>
    public TextArea TextArea { get; }

    /// <summary>
    /// Start of the text range in which the window stays open. The text from here to the caret is
    /// what selects an entry by typing.
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>End of the text range in which the window stays open.</summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// Whether the window should expect a single text insertion at the start offset that belongs
    /// before the completion region rather than in it. Reset to false when that insertion occurs.
    /// </summary>
    public bool ExpectInsertionBeforeStart { get; set; }

    /// <summary>Whether the window was placed above the anchor line.</summary>
    protected bool IsUp { get; private set; }

    public bool IsOpen => _isOpen;

    /// <summary>Raised after the window closed, for whichever reason.</summary>
    public event EventHandler? Closed;

    /// <summary>The element put on the editor's overlay layer.</summary>
    protected internal Border Root { get; }

    protected TextEditor Editor => TextArea.Editor;

    /// <summary>Shows the window anchored at <see cref="StartOffset"/>.</summary>
    public void Show()
    {
        if (_isOpen)
        {
            return;
        }
        _isOpen = true;
        AttachEvents();
        Editor.ShowOverlay(Root);
        UpdatePosition();
    }

    /// <summary>Closes the window and detaches everything it attached. Closing twice is harmless.</summary>
    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }
        _isOpen = false;
        DetachEvents();
        Editor.HideOverlay(Root);
        OnClosed(EventArgs.Empty);
    }

    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);

    /// <summary>
    /// Attaches the window to the text area. The original closes other completion windows of the
    /// same type first, so at most one of a kind is open.
    /// </summary>
    protected virtual void AttachEvents()
    {
        _document = TextArea.Document;
        _document.Changed += OnDocumentChangedForOffsets;
        TextArea.SelectionChanged += OnEditingStateChangedForPosition;
        Editor.DocumentChanged += OnEditorDocumentChanged;
        Editor.Surface.LinesChanged += OnSurfaceLinesChanged;

        foreach (var handler in TextArea.StackedInputHandlers.OfType<InputHandler>().ToArray())
        {
            if (handler.Window.GetType() == GetType())
            {
                TextArea.PopStackedInputHandler(handler);
            }
        }
        TextArea.PushStackedInputHandler(_inputHandler);
    }

    protected virtual void DetachEvents()
    {
        _document.Changed -= OnDocumentChangedForOffsets;
        TextArea.SelectionChanged -= OnEditingStateChangedForPosition;
        Editor.DocumentChanged -= OnEditorDocumentChanged;
        Editor.Surface.LinesChanged -= OnSurfaceLinesChanged;
        TextArea.PopStackedInputHandler(_inputHandler);
    }

    /// <summary>
    /// A stacked handler that feeds keys to the window while the editor keeps the focus. Popping
    /// it - by any other handler taking over - closes the window.
    /// </summary>
    private sealed class InputHandler(CompletionWindowBase window) : TextAreaStackedInputHandler(window.TextArea)
    {
        internal CompletionWindowBase Window { get; } = window;

        public override void Detach()
        {
            base.Detach();
            Window.Close();
        }

        public override void OnPreviewKeyDown(KeyEventArgs e) => Window.OnPreviewKeyDown(e);

        public override void OnPreviewKeyUp(KeyEventArgs e) => Window.OnPreviewKeyUp(e);
    }

    /// <summary>Escape closes; a derived window feeds the remaining keys to its content.</summary>
    protected virtual void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    protected virtual void OnPreviewKeyUp(KeyEventArgs e)
    {
    }

    private void OnEditorDocumentChanged(object? sender, EventArgs e) => Close();

    private void OnSurfaceLinesChanged(Aprillz.MewUI.Text.ITextViewHost host)
    {
        // Scrolling far enough that the anchor leaves the viewport closes the window; any other
        // layout change just moves it along with the anchor.
        var viewport = Editor.Surface.TextViewportBounds;
        var anchor = AnchorRect();
        if (anchor.Bottom < viewport.Y || anchor.Y > viewport.Y + viewport.Height)
        {
            Close();
        }
        else
        {
            UpdatePosition();
        }
    }

    private void OnEditingStateChangedForPosition(object? sender, EventArgs e)
    {
        if (_isOpen)
        {
            UpdatePosition();
        }
    }

    private Rect AnchorRect()
    {
        int anchorOffset = Math.Clamp(
            StartOffset != TextArea.Caret.Offset ? StartOffset : TextArea.Caret.Offset,
            0, _document.TextLength);
        return Editor.Surface.GetCharRectInWindow(anchorOffset);
    }

    /// <summary>
    /// Places the window below the anchor line, or above it when there is no room below. The
    /// overlay is clipped at the editor's bounds, so the working area is the editor itself.
    /// </summary>
    protected void UpdatePosition()
    {
        if (Editor.OverlayHostBounds is not Rect host || host.Width <= 0)
        {
            return;
        }
        var anchor = AnchorRect();
        Root.Measure(new Size(host.Width, host.Height));
        var desired = Root.DesiredSize;

        double x = anchor.X - host.X;
        if (x + desired.Width > host.Width)
        {
            x = Math.Max(0, host.Width - desired.Width);
        }
        double y = anchor.Y + anchor.Height - host.Y;
        if (y + desired.Height > host.Height && anchor.Y - host.Y >= desired.Height)
        {
            y = anchor.Y - host.Y - desired.Height;
            IsUp = true;
        }
        else
        {
            IsUp = false;
        }
        Root.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
    }

    /// <summary>
    /// Follows document changes: a removal immediately in front of the completion segment closes
    /// the window (backspace after dot-completion), the start stays put before insertions unless
    /// <see cref="ExpectInsertionBeforeStart"/>, and the end rides after them, so typing grows the
    /// completion region.
    /// </summary>
    private void OnDocumentChangedForOffsets(object? sender, DocumentChangeEventArgs e)
    {
        if (e.Offset + e.RemovalLength == StartOffset && e.RemovalLength > 0)
        {
            Close();
        }
        if (e.Offset == StartOffset && e.RemovalLength == 0 && ExpectInsertionBeforeStart)
        {
            StartOffset = e.GetNewOffset(StartOffset, AnchorMovementType.AfterInsertion);
            ExpectInsertionBeforeStart = false;
        }
        else
        {
            StartOffset = e.GetNewOffset(StartOffset, AnchorMovementType.BeforeInsertion);
        }
        EndOffset = e.GetNewOffset(EndOffset, AnchorMovementType.AfterInsertion);
    }
}
