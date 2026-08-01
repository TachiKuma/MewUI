using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>Read-only, virtualized text surface for syntax and diagnostic extensions.</summary>
public sealed class SyntaxViewer : Control
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<SyntaxViewer>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, value) => self.ReplaceDocument(value));

    public static readonly MewProperty<bool> WrapProperty =
        MewProperty<bool>.Register<SyntaxViewer>(nameof(Wrap), false,
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

    public SyntaxViewer()
    {
        Cursor = CursorType.IBeam;
        Extensions = new TextViewExtensionPipeline();
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

    public TextViewExtensionPipeline Extensions { get; }
    public int SelectionStart => Math.Min(_anchor, _caret);
    public int SelectionLength => Math.Abs(_caret - _anchor);
    public string SelectedText => SelectionLength == 0
        ? string.Empty
        : _document.GetText(SelectionStart, SelectionLength);
    public double VerticalOffset => _verticalOffset;
    public double HorizontalOffset => _horizontalOffset;
    public IClipboardService? ClipboardService { get; set; }
    internal int MaterializedLineCount => _view?.MaterializedLines.Count ?? 0;

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
        Extensions.Revision++;
        ResetView();
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
                    ? [span]
                    : [];
                double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
                var origin = new Point(
                    _contentBounds.X - _horizontalOffset,
                    _contentBounds.Y + documentY - _verticalOffset);
                var options = new TextDrawOptions(Theme.Palette.WindowText, paint, Owner: line);
                line.Draw(context.Text, origin, in options);
            }
        }
        finally
        {
            context.Restore();
        }
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
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
        _verticalOffset = Math.Clamp(
            _verticalOffset - e.Delta.Y * Theme.Metrics.ScrollWheelStep,
            0,
            maximum);
        UpdateViewport();
        InvalidateVisual();
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
        base.OnDispose();
    }
}
