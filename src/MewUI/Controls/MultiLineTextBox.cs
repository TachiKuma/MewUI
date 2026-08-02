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
public sealed class MultiLineTextBox : TextBase, IVisualTreeHost
{
    public static readonly MewProperty<bool> WrapProperty =
        MewProperty<bool>.Register<MultiLineTextBox>(nameof(Wrap), true,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, value) => self.OnWrapChanged(value));

    private readonly ScrollBar _verticalScrollBar;
    private readonly ScrollBar _horizontalScrollBar;
    private TextViewLayout? _view;
    private IGraphicsFactory? _viewFactory;
    private Rect _contentBounds;
    private double _verticalOffset;
    private double _horizontalOffset;
    private double _preferredCaretX = double.NaN;
    private bool _dragSelecting;

    static MultiLineTextBox()
    {
        FocusableProperty.OverrideDefaultValue<MultiLineTextBox>(true);
    }

    public MultiLineTextBox()
        : this(new EditableTextDocument())
    {
    }

    public MultiLineTextBox(EditableTextDocument document)
        : base(document)
    {
        Extensions = new TextViewExtensionPipeline();
        _document.Changed += OnDocumentChanged;
        _editor.StateChanged += OnEditorStateChanged;

        _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _verticalScrollBar.Parent = this;
        _horizontalScrollBar.Parent = this;
        _verticalScrollBar.ValueChanged += value => SetVerticalOffset(value);
        _horizontalScrollBar.ValueChanged += value => SetHorizontalOffset(value);
    }

    public bool Wrap
    {
        get => GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;
    public EditableTextDocument Document => _document;
    public TextViewExtensionPipeline Extensions { get; }
    public IReadOnlyList<TextLineLayout> VisibleTextLines
        => _view?.MaterializedLines ?? Array.Empty<TextLineLayout>();
    public Rect TextViewportBounds => _contentBounds;

    internal int MaterializedLineCount => _view?.MaterializedLines.Count ?? 0;
    internal int MaterializedCharacterCount
        => _view?.MaterializedLines.Sum(static line => line.LogicalLine.Length) ?? 0;
    internal int MaterializedVisualLineCount
        => _view?.MaterializedLines.Sum(static line => line.VisualLines.Count) ?? 0;
    internal bool IsVerticalScrollBarVisible => _verticalScrollBar.IsVisible;
    internal bool IsHorizontalScrollBarVisible => _horizontalScrollBar.IsVisible;

    public event Action? EditingStateChanged;
    public event Action<bool>? WrapChanged;

    /// <summary>Re-runs registered classifiers, generators, projections, and adornments.</summary>
    public void InvalidateTextView()
    {
        Extensions.Revision++;
        ResetView();
    }

    private void DrawCompositionUnderlines(IGraphicsContext context)
    {
        if (!_editor.IsComposing || _compositionLength <= 0)
        {
            return;
        }

        var color = Theme.Palette.WindowText;
        int index = 0;
        while (index < _compositionLength)
        {
            var attr = GetCompositionAttr(index);
            var startRect = GetCharRectInWindow(_compositionStart + index);
            double lineY = startRect.Y;

            int segmentEnd = index + 1;
            var endRect = GetCharRectInWindow(_compositionStart + segmentEnd);
            while (segmentEnd < _compositionLength && GetCompositionAttr(segmentEnd) == attr && endRect.Y == lineY)
            {
                segmentEnd++;
                endRect = GetCharRectInWindow(_compositionStart + segmentEnd);
            }

            // A wrapped segment ends past the visual line; underline to the viewport text edge instead.
            double endX = endRect.Y == lineY ? endRect.X : _contentBounds.Right;
            DrawCompositionUnderline(context, startRect.X, endX, lineY + startRect.Height, color, attr);
            index = segmentEnd;
        }
    }

    private CompositionAttr GetCompositionAttr(int offsetInComposition)
        => _compositionAttributes is { Length: > 0 } attrs && offsetInComposition < attrs.Length
            ? attrs[offsetInComposition]
            : CompositionAttr.Input;

    private static void DrawCompositionUnderline(
        IGraphicsContext context, double startX, double endX, double y, Color color, CompositionAttr attr)
    {
        double thickness = attr is CompositionAttr.TargetConverted or CompositionAttr.TargetNotConverted ? 2 : 1;
        bool dashed = attr is CompositionAttr.Input or CompositionAttr.TargetNotConverted;

        if (!dashed)
        {
            context.DrawLine(new Point(startX, y), new Point(endX, y), color, thickness, pixelSnap: true);
            return;
        }

        const double DASH = 3;
        const double GAP = 2;
        double x = startX;
        while (x < endX)
        {
            double dashEnd = Math.Min(x + DASH, endX);
            context.DrawLine(new Point(x, y), new Point(dashEnd, y), color, thickness, pixelSnap: true);
            x = dashEnd + GAP;
        }
    }

    public override Rect GetCharRectInWindow(int charIndex)
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

        DrawCompositionUnderlines(context);

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

    private void OnDocumentChanged(TextChange change)
    {
        _view?.Invalidate(change);
    }

    private void OnEditorStateChanged()
    {
        _preferredCaretX = double.NaN;
        ResetCaretBlink();
        InvalidateVisual();
        EditingStateChanged?.Invoke();
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
        double preferredCaretX = _preferredCaretX;
        int sourceLine = _document.GetLineByOffset(_editor.CaretPosition).LineNumber;
        var hit = _view.HitTest(new Point(
            preferredCaretX - _horizontalOffset,
            caret.Y - _verticalOffset + caret.Height / 2 + direction * Math.Max(1, caret.Height)));
        int target = hit.DocumentOffset;
        double targetVisualY = caret.Y + direction * Math.Max(1, caret.Height);
        if (target > 0 && _view.GetCaretBounds(target).Y > targetVisualY + 0.5)
        {
            // A soft-wrap boundary has one document offset but two visual affinities.
            // The editor stores offsets only, so choose the preceding grapheme when a
            // hit at the end of the target row resolves to the following visual row.
            target = _editor.GetPreviousCaretPosition(target);
        }
        _editor.SetCaret(target, extend);
        if (hit.LineNumber == sourceLine)
        {
            _preferredCaretX = preferredCaretX;
        }
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

    private protected override void EnsureCaretVisible()
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
        _view?.Dispose();
        _document.Changed -= OnDocumentChanged;
        _editor.StateChanged -= OnEditorStateChanged;
        _verticalScrollBar.Dispose();
        _horizontalScrollBar.Dispose();
        base.OnDispose();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_verticalScrollBar) && visitor(_horizontalScrollBar);
}
