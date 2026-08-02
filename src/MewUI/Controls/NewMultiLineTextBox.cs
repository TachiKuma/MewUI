using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;
using System.Globalization;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Multi-line editor built on the extensible text view engine.
/// It does not use the legacy Controls.Text formatter, view, or measurement caches.
/// </summary>
public sealed class NewMultiLineTextBox : Control, ITextCompositionClient, ITextInputClient, IVisualTreeHost
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<NewMultiLineTextBox>(nameof(Text), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, value) => self.ApplyExternalText(value));

    public static readonly MewProperty<string> PlaceholderProperty =
        MewProperty<string>.Register<NewMultiLineTextBox>(nameof(Placeholder), string.Empty,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> IsReadOnlyProperty =
        MewProperty<bool>.Register<NewMultiLineTextBox>(nameof(IsReadOnly), false,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> AcceptTabProperty =
        MewProperty<bool>.Register<NewMultiLineTextBox>(nameof(AcceptTab), false);

    public static readonly MewProperty<bool> WrapProperty =
        MewProperty<bool>.Register<NewMultiLineTextBox>(nameof(Wrap), true,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, value) => self.OnWrapChanged(value));

    public static readonly MewProperty<int> MaxLengthProperty =
        MewProperty<int>.Register<NewMultiLineTextBox>(nameof(MaxLength), 0);

    public static readonly MewProperty<ImeMode> ImeModeProperty =
        MewProperty<ImeMode>.Register<NewMultiLineTextBox>(nameof(ImeMode), ImeMode.Auto);

    private static readonly MewPropertyKey<int> SelectionStartPropertyKey =
        MewProperty<int>.RegisterReadOnly<NewMultiLineTextBox>(nameof(SelectionStart), 0);

    public static readonly MewProperty<int> SelectionStartProperty = SelectionStartPropertyKey.Property;

    private static readonly MewPropertyKey<int> SelectionLengthPropertyKey =
        MewProperty<int>.RegisterReadOnly<NewMultiLineTextBox>(nameof(SelectionLength), 0);

    public static readonly MewProperty<int> SelectionLengthProperty = SelectionLengthPropertyKey.Property;

    private readonly EditableTextDocument _document;
    private readonly TextEditorSession _editor;
    private readonly ScrollBar _verticalScrollBar;
    private readonly ScrollBar _horizontalScrollBar;
    private TextViewLayout? _view;
    private IGraphicsFactory? _viewFactory;
    private Rect _contentBounds;
    private double _verticalOffset;
    private double _horizontalOffset;
    private double _preferredCaretX = double.NaN;
    private bool _syncingText;
    private bool _dragSelecting;
    private bool _suppressNewLineInput;
    private bool _suppressTabInput;
    private string _textSnapshot = string.Empty;
    private long _textSnapshotVersion = -1;
    private int _compositionStart;
    private int _compositionLength;
    private CompositionAttr[]? _compositionAttributes;
    private DispatcherTimer? _caretTimer;
    private bool _caretVisible = true;

    static NewMultiLineTextBox()
    {
        FocusableProperty.OverrideDefaultValue<NewMultiLineTextBox>(true);
    }

    public NewMultiLineTextBox()
        : this(new EditableTextDocument())
    {
    }

    public NewMultiLineTextBox(EditableTextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Cursor = CursorType.IBeam;
        Extensions = new TextViewExtensionPipeline();
        _editor = new TextEditorSession(_document);
        _document.Changed += OnDocumentChanged;
        _editor.StateChanged += OnEditorStateChanged;

        _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _verticalScrollBar.Parent = this;
        _horizontalScrollBar.Parent = this;
        _verticalScrollBar.ValueChanged += value => SetVerticalOffset(value);
        _horizontalScrollBar.ValueChanged += value => SetHorizontalOffset(value);

        if (_document.TextLength > 0)
        {
            _syncingText = true;
            try
            {
                SetValue(TextProperty, GetTextSnapshot());
            }
            finally
            {
                _syncingText = false;
            }
        }
    }

    public string Text
    {
        get => GetTextSnapshot();
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool AcceptTab
    {
        get => GetValue(AcceptTabProperty);
        set => SetValue(AcceptTabProperty, value);
    }

    public bool Wrap
    {
        get => GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, Math.Max(0, value));
    }

    public ImeMode ImeMode
    {
        get => GetValue(ImeModeProperty);
        set => SetValue(ImeModeProperty, value);
    }

    public int CaretPosition
    {
        get => _editor.CaretPosition;
        set
        {
            _editor.SetCaret(value);
            EnsureCaretVisible();
        }
    }

    public int SelectionStart => GetValue(SelectionStartProperty);
    public int SelectionLength => GetValue(SelectionLengthProperty);
    public string SelectedText => SelectionLength == 0
        ? string.Empty
        : _document.GetText(SelectionStart, SelectionLength);
    public bool CanUndo => _editor.CanUndo;
    public bool CanRedo => _editor.CanRedo;
    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;
    public EditableTextDocument Document => _document;
    public TextViewExtensionPipeline Extensions { get; }
    public IReadOnlyList<TextLineLayout> VisibleTextLines
        => _view?.MaterializedLines ?? Array.Empty<TextLineLayout>();
    public Rect TextViewportBounds => _contentBounds;

    /// <summary>Optional clipboard override for hosted editors and tests.</summary>
    public IClipboardService? ClipboardService { get; set; }

    internal int MaterializedLineCount => _view?.MaterializedLines.Count ?? 0;
    internal int MaterializedCharacterCount
        => _view?.MaterializedLines.Sum(static line => line.LogicalLine.Length) ?? 0;
    internal int MaterializedVisualLineCount
        => _view?.MaterializedLines.Sum(static line => line.VisualLines.Count) ?? 0;
    internal bool IsVerticalScrollBarVisible => _verticalScrollBar.IsVisible;
    internal bool IsHorizontalScrollBarVisible => _horizontalScrollBar.IsVisible;

    public event Action<string>? TextChanged;
    public event Action? EditingStateChanged;
    public event Action<bool>? WrapChanged;
    public event Action<TextInputEventArgs>? TextInput;
    public event Action<TextCompositionEventArgs>? TextCompositionStart;
    public event Action<TextCompositionEventArgs>? TextCompositionUpdate;
    public event Action<TextCompositionEventArgs>? TextCompositionEnd;

    /// <summary>Re-runs registered classifiers, generators, projections, and adornments.</summary>
    public void InvalidateTextView()
    {
        Extensions.Revision++;
        ResetView();
    }

    bool ITextCompositionClient.IsComposing => _editor.IsComposing;
    int ITextCompositionClient.CompositionStartIndex => _compositionStart;

    public void Select(int start, int length) => _editor.SetSelection(start, length);
    public void SelectAll() => _editor.SelectAll();

    public void AppendText(string? text, bool scrollToCaret = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        _editor.SetCaret(_document.TextLength);
        InsertText(text);
        if (scrollToCaret)
        {
            EnsureCaretVisible();
        }
    }

    public void ReplaceSelection(string? text)
    {
        if (IsReadOnly)
        {
            return;
        }
        InsertText(text);
        EnsureCaretVisible();
    }

    public void Undo()
    {
        if (!IsReadOnly)
        {
            _editor.Undo();
            EnsureCaretVisible();
        }
    }

    public void Redo()
    {
        if (!IsReadOnly)
        {
            _editor.Redo();
            EnsureCaretVisible();
        }
    }

    public void Copy()
    {
        if (SelectionLength > 0)
        {
            TrySetClipboardText(SelectedText);
        }
    }

    public void Cut()
    {
        if (IsReadOnly || SelectionLength == 0)
        {
            return;
        }
        Copy();
        _editor.ReplaceSelection(string.Empty);
    }

    public void Paste()
    {
        if (!IsReadOnly && TryGetClipboardText(out string text))
        {
            InsertText(text);
        }
    }

    public Rect GetCharRectInWindow(int charIndex)
    {
        EnsureView();
        if (_view is null)
        {
            return Rect.Empty;
        }
        var caret = _view.GetCaretBounds(Math.Clamp(charIndex, 0, _document.TextLength));
        return new Rect(
            _contentBounds.X + caret.X - _horizontalOffset,
            _contentBounds.Y + caret.Y - _verticalOffset,
            caret.Width,
            caret.Height);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double lineHeight = Math.Max(16, FontSize * 1.4);
        double width = double.IsPositiveInfinity(availableSize.Width) ? 240 : availableSize.Width;
        double height = double.IsPositiveInfinity(availableSize.Height)
            ? Math.Min(400, Math.Max(3, _document.LineCount) * lineHeight + Padding.VerticalThickness)
            : availableSize.Height;
        return new Size(Math.Max(40, width), Math.Max(lineHeight, height));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _contentBounds = GetEditorContentBounds();
        UpdateViewport();
        ArrangeScrollBars();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);
        _contentBounds = GetEditorContentBounds();
        UpdateViewport();

        context.Save();
        try
        {
            context.SetClip(LayoutRounding.MakeClipRect(_contentBounds, GetDpi() / 96.0));
            if (_document.TextLength == 0 && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
            {
                DrawPlaceholder(context);
            }
            else
            {
                DrawDocument(context);
            }
        }
        finally
        {
            context.Restore();
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (_verticalScrollBar.IsVisible)
        {
            _verticalScrollBar.Render(context);
        }
        if (_horizontalScrollBar.IsVisible)
        {
            _horizontalScrollBar.Render(context);
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }
        if (_verticalScrollBar.IsVisible && _verticalScrollBar.Bounds.Contains(point))
        {
            return _verticalScrollBar;
        }
        if (_horizontalScrollBar.IsVisible && _horizontalScrollBar.Bounds.Contains(point))
        {
            return _horizontalScrollBar;
        }
        return Bounds.Contains(point) ? this : null;
    }

    private void DrawDocument(IGraphicsContext context)
    {
        if (_view is null)
        {
            return;
        }
        var selection = _editor.Selection;
        foreach (var line in _view.MaterializedLines)
        {
            TextPaintSpan[] paint = CreatePaintSpans(line, selection);
            double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
            var origin = new Point(
                _contentBounds.X - _horizontalOffset,
                _contentBounds.Y + documentY - _verticalOffset);
            var options = new TextDrawOptions(
                Theme.Palette.WindowText,
                paint,
                Owner: line);
            line.Draw(context.Text, origin, in options);
        }

        if (IsFocused && _caretVisible)
        {
            var caret = GetCharRectInWindow(_editor.CaretPosition);
            context.FillRectangle(new Rect(caret.X, caret.Y, 1, Math.Max(1, caret.Height)), Theme.Palette.WindowText);
        }
    }

    private TextPaintSpan[] CreatePaintSpans(TextLineLayout line, TextRange selection)
    {
        var spans = new List<TextPaintSpan>(2);
        int lineStart = line.LogicalLine.Offset;
        int lineEnd = lineStart + line.LogicalLine.Length;
        if (TextSelectionPresentation.TryCreateSpan(
                line.LogicalLine,
                selection,
                Theme.Palette.SelectionText,
                Theme.Palette.SelectionBackground,
                out var selectionSpan))
        {
            spans.Add(selectionSpan);
        }

        if (_editor.IsComposing)
        {
            int compositionEnd = _compositionStart + _compositionLength;
            int start = Math.Max(_compositionStart, lineStart);
            int end = Math.Min(compositionEnd, lineEnd);
            if (end > start)
            {
                spans.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Decoration: TextDecoration.Underline));
            }
        }
        return spans.ToArray();
    }

    private void DrawPlaceholder(IGraphicsContext context)
    {
        var request = CreateTextRequest(Placeholder, TextWrapping.NoWrap, _contentBounds.Width);
        var layout = GetGraphicsFactory().TextEngine.GetOrCreateLayout(request, TextLayoutCachePolicy.Owner, this);
        var options = new TextDrawOptions(Theme.Palette.PlaceholderText, Owner: this);
        context.Text.Draw(layout, _contentBounds.Position, in options);
    }

    private void EnsureView()
    {
        var factory = GetGraphicsFactory();
        if (_view is not null && ReferenceEquals(_viewFactory, factory))
        {
            return;
        }
        _view?.Dispose();
        _viewFactory = factory;
        _view = new TextViewLayout(
            factory.TextEngine,
            _document,
            new TextRunStyle(FontFamily, FontSize, FontWeight),
            new TextParagraphStyle
            {
                Wrapping = Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Culture = System.Globalization.CultureInfo.CurrentUICulture
            },
            Extensions,
            dpi: GetDpi());
    }

    private void UpdateViewport()
    {
        EnsureView();
        if (_view is null || _contentBounds.Width <= 0 || _contentBounds.Height <= 0)
        {
            return;
        }
        _view.SetViewport(new TextViewport(
            _contentBounds.Width,
            _contentBounds.Height,
            _horizontalOffset,
            _verticalOffset));
        SetVerticalOffset(_verticalOffset, false);
        SetHorizontalOffset(_horizontalOffset, false);
    }

    private void ArrangeScrollBars()
    {
        if (_view is null)
        {
            return;
        }
        double thickness = Theme.Metrics.ScrollBarHitThickness;
        double extentHeight = _view.ExtentHeight;
        double extentWidth = _view.ExtentWidth;
        bool vertical = extentHeight > _contentBounds.Height + 0.5;
        bool horizontal = !Wrap && extentWidth > _contentBounds.Width + 0.5;
        _verticalScrollBar.IsVisible = vertical;
        _horizontalScrollBar.IsVisible = horizontal;

        if (vertical)
        {
            _verticalScrollBar.Minimum = 0;
            _verticalScrollBar.Maximum = Math.Max(0, extentHeight - _contentBounds.Height);
            _verticalScrollBar.ViewportSize = _contentBounds.Height;
            _verticalScrollBar.Value = _verticalOffset;
            _verticalScrollBar.Arrange(new Rect(Bounds.Right - thickness, Bounds.Y, thickness, Bounds.Height));
        }
        else
        {
            _verticalScrollBar.Arrange(Rect.Empty);
        }
        if (horizontal)
        {
            _horizontalScrollBar.Minimum = 0;
            _horizontalScrollBar.Maximum = Math.Max(0, extentWidth - _contentBounds.Width);
            _horizontalScrollBar.ViewportSize = _contentBounds.Width;
            _horizontalScrollBar.Value = _horizontalOffset;
            _horizontalScrollBar.Arrange(new Rect(Bounds.X, Bounds.Bottom - thickness, Bounds.Width, thickness));
        }
        else
        {
            _horizontalScrollBar.Arrange(Rect.Empty);
        }
    }

    private TextLayoutRequest CreateTextRequest(string text, TextWrapping wrapping, double width)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = GetDpi(),
            DefaultStyle = new TextRunStyle(FontFamily, FontSize, FontWeight),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = width,
                Wrapping = wrapping,
                Culture = System.Globalization.CultureInfo.CurrentUICulture
            }
        };

    private Rect GetEditorContentBounds()
    {
        var snapped = GetSnappedBorderBounds(Bounds);
        double border = GetBorderVisualInset();
        return LayoutRounding.SnapViewportRectToPixels(
            snapped.Deflate(new Thickness(border)).Deflate(Padding),
            GetDpi() / 96.0);
    }

    private void ApplyExternalText(string value)
    {
        if (_syncingText)
        {
            return;
        }
        _syncingText = true;
        try
        {
            _editor.CommitComposition();
            string normalized = EditableTextDocument.NormalizeNewLines(value ?? string.Empty);
            _document.SetText(normalized);
            _textSnapshot = normalized;
            _textSnapshotVersion = _document.Version;
            _editor.ClearHistory();
            _editor.SetCaret(Math.Min(_editor.CaretPosition, _document.TextLength));
        }
        finally
        {
            _syncingText = false;
        }
    }

    private void OnDocumentChanged(TextChange change)
    {
        _textSnapshotVersion = -1;
        _view?.Invalidate(change);
        string? currentText = null;
        if (!_syncingText && (HasPropertyBinding(TextProperty.Id) || TextChanged is not null))
        {
            _syncingText = true;
            try
            {
                currentText = _document.ToString();
                SetValue(TextProperty, currentText);
            }
            finally
            {
                _syncingText = false;
            }
        }
        if (TextChanged is { } textChanged)
        {
            currentText ??= _document.ToString();
            textChanged(currentText);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private string GetTextSnapshot()
    {
        if (_textSnapshotVersion != _document.Version)
        {
            _textSnapshot = _document.ToString();
            _textSnapshotVersion = _document.Version;
        }
        return _textSnapshot;
    }

    private void OnEditorStateChanged()
    {
        var selection = _editor.Selection;
        SetValue(SelectionStartPropertyKey, selection.Start);
        SetValue(SelectionLengthPropertyKey, selection.Length);
        _preferredCaretX = double.NaN;
        ResetCaretBlink();
        InvalidateVisual();
        EditingStateChanged?.Invoke();
    }

    private void InsertText(string? value)
    {
        string text = EditableTextDocument.NormalizeNewLines(value ?? string.Empty);
        if (MaxLength > 0)
        {
            int remaining = MaxLength - (_document.TextLength - _editor.Selection.Length);
            if (remaining <= 0)
            {
                return;
            }
            if (text.Length > remaining)
            {
                text = TruncateAtTextElementBoundary(text, remaining);
            }
        }
        if (text.Length > 0)
        {
            _editor.ReplaceSelection(text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }
        if (e.PrimaryKey && HandlePrimaryKey(e))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                _editor.MoveLogical(LogicalDirection.Backward, e.ShiftKey, e.ControlKey);
                break;
            case Key.Right:
                _editor.MoveLogical(LogicalDirection.Forward, e.ShiftKey, e.ControlKey);
                break;
            case Key.Up:
                MoveCaretVertical(-1, e.ShiftKey);
                break;
            case Key.Down:
                MoveCaretVertical(1, e.ShiftKey);
                break;
            case Key.Home:
                MoveToLineEdge(true, e.ShiftKey);
                break;
            case Key.End:
                MoveToLineEdge(false, e.ShiftKey);
                break;
            case Key.Backspace when !IsReadOnly:
                _editor.Backspace(e.ControlKey);
                break;
            case Key.Delete when !IsReadOnly:
                _editor.Delete(e.ControlKey);
                break;
            case Key.Enter when !IsReadOnly:
                InsertText("\n");
                _suppressNewLineInput = true;
                break;
            case Key.Tab when !IsReadOnly && AcceptTab:
                InsertText("\t");
                _suppressTabInput = true;
                break;
            default:
                return;
        }
        e.Handled = true;
        EnsureCaretVisible();
    }

    private bool HandlePrimaryKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.A:
                SelectAll();
                return true;
            case Key.C:
                Copy();
                return true;
            case Key.X:
                Cut();
                return true;
            case Key.V:
                Paste();
                return true;
            case Key.Z:
                if (e.ShiftKey) Redo(); else Undo();
                return true;
            case Key.Y:
                Redo();
                return true;
            case Key.Home:
                _editor.SetCaret(0, e.ShiftKey);
                return true;
            case Key.End:
                _editor.SetCaret(_document.TextLength, e.ShiftKey);
                return true;
            default:
                return false;
        }
    }

    private void MoveToLineEdge(bool start, bool extend)
    {
        var line = _document.GetLineByOffset(_editor.CaretPosition);
        _editor.SetCaret(start ? line.Offset : line.Offset + line.Length, extend);
    }

    private void MoveCaretVertical(int direction, bool extend)
    {
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var caret = _view.GetCaretBounds(_editor.CaretPosition);
        if (double.IsNaN(_preferredCaretX))
        {
            _preferredCaretX = caret.X;
        }
        var hit = _view.HitTest(new Point(
            _preferredCaretX - _horizontalOffset,
            caret.Y - _verticalOffset + direction * Math.Max(1, caret.Height)));
        _editor.SetCaret(hit.DocumentOffset, extend);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }
        Focus();
        SetCaretFromPoint(e.Position, e.ShiftKey);
        _dragSelecting = true;
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }
        e.Handled = true;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        SetCaretFromPoint(e.Position, false);
        _editor.SelectWordAt(_editor.CaretPosition);
        EnsureCaretVisible();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragSelecting || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }
        AutoScroll(e.Position);
        SetCaretFromPoint(e.Position, true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left)
        {
            _dragSelecting = false;
            if (FindVisualRoot() is Window window)
            {
                window.ReleaseMouseCapture();
            }
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!e.Handled && e.Delta.Y != 0)
        {
            SetVerticalOffset(_verticalOffset - e.Delta.Y * Theme.Metrics.ScrollWheelStep);
            e.Handled = true;
        }
    }

    private void SetCaretFromPoint(Point point, bool extend)
    {
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var hit = _view.HitTest(new Point(point.X - _contentBounds.X, point.Y - _contentBounds.Y));
        _editor.SetCaret(hit.DocumentOffset, extend);
        EnsureCaretVisible();
    }

    private void AutoScroll(Point point)
    {
        if (point.Y < _contentBounds.Y)
        {
            SetVerticalOffset(_verticalOffset + point.Y - _contentBounds.Y);
        }
        else if (point.Y > _contentBounds.Bottom)
        {
            SetVerticalOffset(_verticalOffset + point.Y - _contentBounds.Bottom);
        }
    }

    private void EnsureCaretVisible()
    {
        if (_contentBounds.IsEmpty)
        {
            return;
        }
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var caret = _view.GetCaretBounds(_editor.CaretPosition);
        double vertical = _verticalOffset;
        double horizontal = _horizontalOffset;
        if (caret.Y < vertical) vertical = caret.Y;
        else if (caret.Bottom > vertical + _contentBounds.Height) vertical = caret.Bottom - _contentBounds.Height;
        if (!Wrap)
        {
            if (caret.X < horizontal) horizontal = caret.X;
            else if (caret.Right > horizontal + _contentBounds.Width) horizontal = caret.Right - _contentBounds.Width;
        }
        SetVerticalOffset(vertical, false);
        SetHorizontalOffset(horizontal, false);
        UpdateViewport();
        InvalidateVisual();
    }

    private void SetVerticalOffset(double value, bool invalidate = true)
    {
        double extent = _view?.ExtentHeight ?? 0;
        double maximum = Math.Max(0, extent - _contentBounds.Height);
        value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_verticalOffset - value) < 0.001) return;
        _verticalOffset = value;
        if (_verticalScrollBar.IsVisible) _verticalScrollBar.Value = value;
        if (invalidate) InvalidateVisual();
    }

    private void SetHorizontalOffset(double value, bool invalidate = true)
    {
        double maximum = _horizontalScrollBar.IsVisible ? _horizontalScrollBar.Maximum : Math.Max(0, value);
        value = Wrap ? 0 : Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_horizontalOffset - value) < 0.001) return;
        _horizontalOffset = value;
        if (_horizontalScrollBar.IsVisible) _horizontalScrollBar.Value = value;
        if (invalidate) InvalidateVisual();
    }

    void ITextInputClient.HandleTextInput(TextInputEventArgs e)
    {
        TextInput?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        string text = e.Text ?? string.Empty;
        if (_suppressNewLineInput && (text.Contains('\r') || text.Contains('\n')))
        {
            _suppressNewLineInput = false;
            e.Handled = true;
            return;
        }
        if (_suppressTabInput && text.Contains('\t'))
        {
            _suppressTabInput = false;
            e.Handled = true;
            return;
        }
        if (_editor.IsComposing) _editor.CommitComposition();
        InsertText(text);
        EnsureCaretVisible();
        e.Handled = true;
    }

    void ITextCompositionClient.HandleTextCompositionStart(TextCompositionEventArgs e)
    {
        TextCompositionStart?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        _editor.BeginComposition();
        _compositionStart = _editor.CaretPosition;
        _compositionLength = 0;
    }

    void ITextCompositionClient.HandleTextCompositionUpdate(TextCompositionEventArgs e)
    {
        TextCompositionUpdate?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        if (!_editor.IsComposing)
        {
            _editor.BeginComposition();
            _compositionStart = _editor.CaretPosition;
        }
        UpdateCompositionText(e.Text);
        _compositionAttributes = e.Attributes;
        EnsureCaretVisible();
    }

    void ITextCompositionClient.HandleTextCompositionEnd(TextCompositionEventArgs e)
    {
        TextCompositionEnd?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        if (!string.IsNullOrEmpty(e.Text))
        {
            UpdateCompositionText(e.Text);
        }
        _editor.CommitComposition();
        _compositionLength = 0;
        _compositionAttributes = null;
        EnsureCaretVisible();
    }

    private void UpdateCompositionText(string? value)
    {
        string text = EditableTextDocument.NormalizeNewLines(value ?? string.Empty);
        if (MaxLength > 0)
        {
            int remaining = MaxLength - (_document.TextLength - _compositionLength);
            text = remaining <= 0
                ? string.Empty
                : TruncateAtTextElementBoundary(text, remaining);
        }
        _editor.UpdateComposition(text);
        _compositionLength = text.Length;
    }

    private static string TruncateAtTextElementBoundary(string text, int maximumLength)
    {
        if (maximumLength <= 0)
        {
            return string.Empty;
        }
        if (text.Length <= maximumLength)
        {
            return text;
        }

        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        int boundaryIndex = Array.BinarySearch(boundaries, maximumLength);
        int length = boundaryIndex >= 0
            ? maximumLength
            : boundaries[Math.Max(0, ~boundaryIndex - 1)];
        return length == 0 ? string.Empty : text[..length];
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        StartCaretBlink();
        if (ImeMode != ImeMode.Auto && FindVisualRoot() is Window { Backend: not null } window)
        {
            window.Backend.SetImeMode(ImeMode);
        }
    }

    protected override void OnLostFocus()
    {
        StopCaretBlink();
        _caretVisible = true;
        if (_editor.IsComposing) _editor.CommitComposition();
        if (ImeMode != ImeMode.Auto && FindVisualRoot() is Window { Backend: not null } window)
        {
            window.Backend.SetImeMode(ImeMode.Auto);
        }
        base.OnLostFocus();
    }

    private void StartCaretBlink()
    {
        StopCaretBlink();
        _caretVisible = true;
        _caretTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500));
        _caretTimer.Tick += OnCaretBlink;
        _caretTimer.Start();
    }

    private void StopCaretBlink()
    {
        if (_caretTimer is null) return;
        _caretTimer.Stop();
        _caretTimer.Tick -= OnCaretBlink;
    }

    private void ResetCaretBlink()
    {
        if (IsFocused) StartCaretBlink();
        else _caretVisible = true;
    }

    private void OnCaretBlink()
    {
        _caretVisible = !_caretVisible;
        InvalidateVisual();
    }

    private void ResetView()
    {
        _view?.Dispose();
        _view = null;
        _viewFactory = null;
        _horizontalOffset = 0;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnWrapChanged(bool value)
    {
        ResetView();
        WrapChanged?.Invoke(value);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            ResetView();
        }
        base.OnMewPropertyChanged(property);
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResetView();
    }

    protected override void OnDispose()
    {
        StopCaretBlink();
        _view?.Dispose();
        _document.Changed -= OnDocumentChanged;
        _editor.StateChanged -= OnEditorStateChanged;
        _verticalScrollBar.Dispose();
        _horizontalScrollBar.Dispose();
        base.OnDispose();
    }

    private bool TrySetClipboardText(string text)
        => (ClipboardService ?? (Application.IsRunning ? Application.Current.PlatformHost.Clipboard : null))
            ?.TrySetText(text) == true;

    private bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        var clipboard = ClipboardService ?? (Application.IsRunning ? Application.Current.PlatformHost.Clipboard : null);
        return clipboard is not null && clipboard.TryGetText(out text);
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_verticalScrollBar) && visitor(_horizontalScrollBar);
}
