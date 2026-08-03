using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>Read-only, virtualized text surface for syntax and diagnostic extensions.</summary>
public sealed class SyntaxViewer : Control, IVisualTreeHost, ITextViewHost
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<SyntaxViewer>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, value) => self.ReplaceDocument(value));

    public static readonly MewProperty<bool> WrapProperty =
        MewProperty<bool>.Register<SyntaxViewer>(nameof(Wrap), false,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ResetView());

    public static readonly MewProperty<int> TabSizeProperty =
        MewProperty<int>.Register<SyntaxViewer>(nameof(TabSize), 4,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ResetView());

    private StringTextDocument _document = new(string.Empty);
    private TextViewLayout? _view;
    private IGraphicsFactory? _viewFactory;
    private Rect _contentBounds;
    private double _verticalOffset;
    private double _horizontalOffset;
    private int _anchor;
    private int _caret;
    private long _documentVersion;
    private bool _dragSelecting;
    private readonly ScrollBar _verticalScrollBar;
    private readonly ScrollBar _horizontalScrollBar;
    private ContextMenu? _defaultContextMenu;

    public SyntaxViewer()
    {
        Cursor = CursorType.IBeam;
        Extensions = new TextViewExtensionPipeline();
        _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _verticalScrollBar.Parent = this;
        _horizontalScrollBar.Parent = this;
        _verticalScrollBar.ValueChanged += value => SetVerticalOffset(value);
        _horizontalScrollBar.ValueChanged += value => SetHorizontalOffset(value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public bool Wrap
    {
        get => GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    /// <summary>Tab width in space characters.</summary>
    public int TabSize
    {
        get => GetValue(TabSizeProperty);
        set => SetValue(TabSizeProperty, value);
    }

    /// <summary>Document whose text the view presents. Replaced whole when <see cref="Text"/> changes.</summary>
    public IReadOnlyTextDocument Document => _document;
    public TextViewExtensionPipeline Extensions { get; }

    /// <summary>Raised after the document was replaced by a <see cref="Text"/> change.</summary>
    public event Action<ITextViewHost>? DocumentChanged;

    public int SelectionStart => Math.Min(_anchor, _caret);
    public int SelectionLength => Math.Abs(_caret - _anchor);
    public string SelectedText => SelectionLength == 0
        ? string.Empty
        : _document.GetText(SelectionStart, SelectionLength);
    public double VerticalOffset => _verticalOffset;
    public double HorizontalOffset => _horizontalOffset;
    public IClipboardService? ClipboardService { get; set; }
    internal int MaterializedLineCount => _view?.MaterializedLines.Count ?? 0;
    internal bool IsVerticalScrollBarVisible => _verticalScrollBar.IsVisible;
    internal bool IsHorizontalScrollBarVisible => _horizontalScrollBar.IsVisible;

    public void Select(int start, int length)
    {
        if (start < 0 || length < 0 || start > _document.TextLength - length)
            throw new ArgumentOutOfRangeException(nameof(start));
        _anchor = start;
        _caret = start + length;
        EnsureSelectionVisible();
        InvalidateVisual();
    }

    public void SelectAll() => Select(0, _document.TextLength);

    public void Copy()
    {
        var clipboard = ClipboardService ?? (Application.IsRunning ? Application.Current.PlatformHost.Clipboard : null);
        if (SelectionLength > 0 && clipboard is not null)
        {
            clipboard.TrySetText(SelectedText);
        }
    }

    /// <summary>Re-runs registered classifiers, generators, projections, and adornments.</summary>
    public void InvalidateTextView()
    {
        // Rebuild instead of reset: extensions re-run against unchanged text, so the reader
        // must stay where they were reading. Only document or metric changes reset scrolling.
        Extensions.Revision++;
        RebuildView();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double width = double.IsPositiveInfinity(availableSize.Width) ? 320 : availableSize.Width;
        double lineHeight = Math.Max(16, FontSize * 1.4);
        double height = double.IsPositiveInfinity(availableSize.Height)
            ? Math.Min(480, Math.Max(3, _document.LineCount) * lineHeight + Padding.VerticalThickness)
            : availableSize.Height;
        return new Size(Math.Max(40, width), Math.Max(lineHeight, height));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _contentBounds = GetContentBounds();
        UpdateViewport();
        ArrangeScrollBars();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(
            context,
            GetSnappedBorderBounds(Bounds),
            Background,
            BorderBrush,
            BorderThickness,
            CornerRadius);
        _contentBounds = GetContentBounds();
        UpdateViewport();
        if (_view is null) return;

        context.Save();
        try
        {
            context.SetClip(LayoutRounding.MakeClipRect(_contentBounds, GetDpi() / 96.0));
            var selection = new TextRange(SelectionStart, SelectionLength);
            foreach (var line in _view.MaterializedLines)
            {
                TextPaintSpan[] paint = TextSelectionPresentation.TryCreateSpan(
                    line.LogicalLine,
                    selection,
                    Theme.Palette.SelectionText,
                    Theme.Palette.SelectionBackground,
                    out var span)
                    ? [span with { Foreground = null }]
                    : [];
                double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
                var origin = new Point(
                    _contentBounds.X - _horizontalOffset,
                    _contentBounds.Y + documentY - _verticalOffset);
                var options = new TextDrawOptions(Theme.Palette.WindowText, paint, Owner: line);
                line.Draw(context.Text, origin, in options);
            }

            // The viewer paints no caret, but caret-layer adornments still belong above every line.
            foreach (var line in _view.MaterializedLines)
            {
                double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
                line.DrawCaretLayer(context.Text, new Point(
                    _contentBounds.X - _horizontalOffset,
                    _contentBounds.Y + documentY - _verticalOffset));
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

    private void EnsureView()
    {
        var factory = GetGraphicsFactory();
        if (_view is not null && ReferenceEquals(_viewFactory, factory)) return;
        _view?.Dispose();
        _viewFactory = factory;
        _view = new TextViewLayout(
            factory.TextEngine,
            _document,
            new TextRunStyle(FontFamily, FontSize, FontWeight),
            new TextParagraphStyle
            {
                Wrapping = Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TabSize = TabSize,
                Culture = System.Globalization.CultureInfo.CurrentUICulture
            },
            Extensions,
            GetDpi());
    }

    private void UpdateViewport()
    {
        EnsureView();
        if (_view is null || _contentBounds.IsEmpty) return;
        double maximum = Math.Max(0, _view.ExtentHeight - _contentBounds.Height);
        _verticalOffset = Math.Clamp(_verticalOffset, 0, maximum);
        if (Wrap) _horizontalOffset = 0;
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

    private void SetVerticalOffset(double value, bool invalidate = true)
    {
        double maximum = _verticalScrollBar.IsVisible
            ? _verticalScrollBar.Maximum
            : Math.Max(0, (_view?.ExtentHeight ?? 0) - _contentBounds.Height);
        value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_verticalOffset - value) < 0.001)
        {
            return;
        }
        _verticalOffset = value;
        if (_verticalScrollBar.IsVisible)
        {
            _verticalScrollBar.Value = value;
        }
        if (invalidate)
        {
            UpdateViewport();
            InvalidateVisual();
        }
    }

    private void SetHorizontalOffset(double value, bool invalidate = true)
    {
        double maximum = _horizontalScrollBar.IsVisible ? _horizontalScrollBar.Maximum : Math.Max(0, value);
        value = Wrap ? 0 : Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_horizontalOffset - value) < 0.001)
        {
            return;
        }
        _horizontalOffset = value;
        if (_horizontalScrollBar.IsVisible)
        {
            _horizontalScrollBar.Value = value;
        }
        if (invalidate)
        {
            UpdateViewport();
            InvalidateVisual();
        }
    }

    private Rect GetContentBounds()
    {
        double border = GetBorderVisualInset();
        return LayoutRounding.SnapViewportRectToPixels(
            GetSnappedBorderBounds(Bounds).Deflate(new Thickness(border)).Deflate(Padding),
            GetDpi() / 96.0);
    }

    private void ReplaceDocument(string value)
    {
        _document = new StringTextDocument(value, ++_documentVersion);
        _anchor = Math.Clamp(_anchor, 0, _document.TextLength);
        _caret = Math.Clamp(_caret, 0, _document.TextLength);
        ResetView();
        DocumentChanged?.Invoke(this);
    }

    private void ResetView()
    {
        _horizontalOffset = 0;
        RebuildView();
    }

    private void RebuildView()
    {
        _view?.Dispose();
        _view = null;
        _viewFactory = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Classifier paint spans are cached per materialized line; theme-dependent
        // classifiers must re-run against the new theme without losing scroll position.
        Extensions.Revision++;
        RebuildView();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Button == MouseButton.Right && ContextMenu == null)
        {
            var menu = _defaultContextMenu ??= new ContextMenu();
            TextContextMenu.Show(menu, this, e.Position,
                copy: new TextMenuCommand(Copy, SelectionLength > 0),
                selectAll: new TextMenuCommand(SelectAll, _document.TextLength > 0));
            e.Handled = true;
            return;
        }
        if (e.Button != MouseButton.Left) return;
        SetCaretFromPoint(e.Position, e.ShiftKey);
        _dragSelecting = true;
        if (FindVisualRoot() is Window window) window.CaptureMouse(this);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragSelecting || !IsMouseCaptured || !e.LeftButton) return;
        SetCaretFromPoint(e.Position, true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left) return;
        _dragSelecting = false;
        if (FindVisualRoot() is Window window) window.ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.Delta.Y == 0 || _view is null) return;
        double maximum = Math.Max(0, _view.ExtentHeight - _contentBounds.Height);
        SetVerticalOffset(
            Math.Clamp(
                _verticalOffset - e.Delta.Y * Theme.Metrics.ScrollWheelStep,
                0,
                maximum));
        e.Handled = true;
    }

    private void SetCaretFromPoint(Point point, bool extend)
    {
        EnsureView();
        if (_view is null) return;
        var hit = _view.HitTest(new Point(point.X - _contentBounds.X, point.Y - _contentBounds.Y));
        _caret = hit.DocumentOffset;
        if (!extend) _anchor = _caret;
        EnsureSelectionVisible();
        InvalidateVisual();
    }

    private void EnsureSelectionVisible()
    {
        EnsureView();
        if (_view is null || _contentBounds.IsEmpty) return;
        var caret = _view.GetCaretBounds(_caret);
        if (caret.Y < _verticalOffset) _verticalOffset = caret.Y;
        else if (caret.Bottom > _verticalOffset + _contentBounds.Height)
            _verticalOffset = caret.Bottom - _contentBounds.Height;
        UpdateViewport();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id || property.Id == FontSizeProperty.Id || property.Id == FontWeightProperty.Id)
            ResetView();
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
        _verticalScrollBar.Dispose();
        _horizontalScrollBar.Dispose();
        base.OnDispose();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_verticalScrollBar) && visitor(_horizontalScrollBar);
}
