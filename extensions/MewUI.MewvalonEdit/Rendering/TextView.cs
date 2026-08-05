using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Where an inserted layer goes relative to its anchor. Mirrors LayerInsertionPosition.</summary>
public enum LayerInsertionPosition
{
    Below,
    Replace,
    Above
}

/// <summary>Rendering-side view of the editor, carrying the extension registrations.</summary>
public sealed class TextView : MewObject
{
    private readonly TextArea textArea;
    private Action<int>? _constructionStarting;
    private Action? _linesChanged;
    private Action? _scrollOffsetChanged;

    internal TextView(TextArea textArea)
    {
        this.textArea = textArea;
        var host = textArea.Editor.Surface;
        // Forwarded rather than exposed directly: the AvalonEdit signatures carry no host argument,
        // and the subscription must survive a document swap, which replaces neither the host nor it.
        host.LineConstructionStarting += (_, firstLine) => _constructionStarting?.Invoke(firstLine);
        host.LinesChanged += _ => _linesChanged?.Invoke();
        host.ScrollOffsetChanged += _ => _scrollOffsetChanged?.Invoke();
    }

    public static readonly MewProperty<Color?> LinkTextForegroundBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(LinkTextForegroundBrush), null,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color?> LinkTextBackgroundBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(LinkTextBackgroundBrush), null,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> LinkTextUnderlineProperty =
        MewProperty<bool>.Register<TextView>(nameof(LinkTextUnderline), true,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color?> NonPrintableCharacterBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(NonPrintableCharacterBrush), null,
            MewPropertyOptions.AffectsRender);

    /// <summary>Colour of link text. Null follows the theme.</summary>
    public Color? LinkTextForegroundBrush
    {
        get => GetValue(LinkTextForegroundBrushProperty);
        set => SetValue(LinkTextForegroundBrushProperty, value);
    }

    /// <summary>Background painted behind link text. Null paints none.</summary>
    public Color? LinkTextBackgroundBrush
    {
        get => GetValue(LinkTextBackgroundBrushProperty);
        set => SetValue(LinkTextBackgroundBrushProperty, value);
    }

    /// <summary>Whether link text is underlined. Clearing it leaves the link clickable.</summary>
    public bool LinkTextUnderline
    {
        get => GetValue(LinkTextUnderlineProperty);
        set => SetValue(LinkTextUnderlineProperty, value);
    }

    /// <summary>Colour of the space, tab and end-of-line markers. Null follows the theme.</summary>
    public Color? NonPrintableCharacterBrush
    {
        get => GetValue(NonPrintableCharacterBrushProperty);
        set => SetValue(NonPrintableCharacterBrushProperty, value);
    }

    public static readonly MewProperty<ColorPen?> ColumnRulerPenProperty =
        MewProperty<ColorPen?>.Register<TextView>(nameof(ColumnRulerPen), null,
            MewPropertyOptions.AffectsRender);

    // Translucent so they read on either theme, which is why these two carry a fixed default
    // instead of the null-follows-the-theme the other colours use.
    public static readonly MewProperty<Color> CurrentLineBackgroundProperty =
        MewProperty<Color>.Register<TextView>(nameof(CurrentLineBackground),
            Color.FromArgb(22, 20, 220, 224), MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<ColorPen> CurrentLineBorderProperty =
        MewProperty<ColorPen>.Register<TextView>(nameof(CurrentLineBorder),
            new ColorPen(Color.FromArgb(52, 0, 255, 110)), MewPropertyOptions.AffectsRender);

    /// <summary>Stroke of the column rule. Null follows the theme.</summary>
    public ColorPen? ColumnRulerPen
    {
        get => GetValue(ColumnRulerPenProperty);
        set => SetValue(ColumnRulerPenProperty, value);
    }

    /// <summary>Background of the line holding the caret.</summary>
    public Color CurrentLineBackground
    {
        get => GetValue(CurrentLineBackgroundProperty);
        set => SetValue(CurrentLineBackgroundProperty, value);
    }

    /// <summary>Stroke of the outline drawn around the line holding the caret.</summary>
    public ColorPen CurrentLineBorder
    {
        get => GetValue(CurrentLineBorderProperty);
        set => SetValue(CurrentLineBorderProperty, value);
    }

    internal double DpiScale => textArea.Editor.EditorDpi / 96.0;

    // Read per paint, so a theme switch repaints in the other palette without any rebuild.
    internal bool IsDarkTheme => textArea.Editor.ThemeIsDark;

    internal ColorPen ResolvedColumnRulerPen
        => ColumnRulerPen ?? new ColorPen(textArea.Editor.ControlBorderColor);

    internal Color ResolvedLinkTextForeground
        => LinkTextForegroundBrush ?? textArea.Editor.AccentColor;

    internal Color ResolvedNonPrintableCharacter
        => NonPrintableCharacterBrush ?? textArea.Editor.PlaceholderColor;

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        // No visual tree here, so AffectsRender invalidates nothing by itself.
        if (property.AffectsRender)
        {
            textArea.Editor.InvalidateTextView();
        }
    }

    /// <summary>Renderers painting into the known layers, in registration order.</summary>
    public IList<IBackgroundRenderer> BackgroundRenderers => textArea.Editor.BackgroundRenderers;

    /// <summary>Transformers restyling ranges of each visual line.</summary>
    public IList<IVisualLineTransformer> LineTransformers => textArea.Editor.LineTransformers;

    /// <summary>Generators replacing document ranges with elements that draw themselves.</summary>
    public IList<VisualLineElementGenerator> ElementGenerators => textArea.Editor.ElementGenerators;

    /// <summary>Extension pipeline of the editing surface, for MewUI-native extensions.</summary>
    public TextViewExtensionPipeline Extensions => textArea.Editor.Surface.Extensions;

    /// <summary>The editing surface as a text view host, for host-neutral extensions.</summary>
    public ITextViewHost Host => textArea.Editor.Surface;

    /// <summary>Document the view presents.</summary>
    public Document.TextDocument Document => textArea.Editor.Document;

    public string FontFamily
    {
        get => textArea.Editor.FontFamily;
        set => textArea.Editor.FontFamily = value;
    }

    public Color Foreground
    {
        get => textArea.Editor.Foreground;
        set => textArea.Editor.Foreground = value;
    }

    /// <summary>Options of the editor this view belongs to.</summary>
    public TextEditorOptions Options => textArea.Editor.Options;

    /// <summary>
    /// Services registered on this view. A colorizer puts its highlighter here so code holding only
    /// the view can find it.
    /// </summary>
    public ServiceContainer Services { get; } = new();

    public object? GetService(Type serviceType) => Services.GetService(serviceType);

    /// <summary>Raised after the document was replaced.</summary>
    public event EventHandler? DocumentChanged
    {
        add => textArea.Editor.DocumentChanged += value;
        remove => textArea.Editor.DocumentChanged -= value;
    }

    /// <summary>Raised after an option changed.</summary>
    public event System.ComponentModel.PropertyChangedEventHandler? OptionChanged
    {
        add => Options.PropertyChanged += value;
        remove => Options.PropertyChanged -= value;
    }

    public void Redraw() => textArea.Editor.InvalidateTextView();

    /// <summary>Rebuilds only the lines overlapping the document range.</summary>
    public void Redraw(int offset, int length) => Host.InvalidateTextRange(offset, length);

    /// <summary>Rebuilds only the lines overlapping the segment.</summary>
    public void Redraw(ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Host.InvalidateTextRange(segment.Offset, segment.Length);
    }

    /// <summary>Rebuilds one laid-out line.</summary>
    public void Redraw(VisualLine visualLine)
    {
        ArgumentNullException.ThrowIfNull(visualLine);
        Host.InvalidateTextRange(visualLine.StartOffset, visualLine.DocumentLength);
    }

    /// <summary>Repaints a layer without rebuilding any line.</summary>
    public void InvalidateLayer(KnownLayer layer) => Host.InvalidateLayer(ToAnchor(layer));

    /// <summary>Draw order of the view, in painting order.</summary>
    public IReadOnlyList<ITextViewLayer> Layers => Host.Layers.Layers;

    /// <summary>Inserts a layer relative to a known anchor.</summary>
    public void InsertLayer(ITextViewLayer layer, KnownLayer anchor, LayerInsertionPosition position)
        => Host.InsertLayer(layer, ToAnchor(anchor), position switch
        {
            LayerInsertionPosition.Replace => TextLayerPosition.Replace,
            LayerInsertionPosition.Above => TextLayerPosition.Above,
            _ => TextLayerPosition.Below
        });

    internal static TextViewLayerAnchor ToAnchor(KnownLayer layer) => layer switch
    {
        KnownLayer.Background => TextViewLayerAnchor.Background,
        KnownLayer.Selection => TextViewLayerAnchor.Selection,
        KnownLayer.Caret => TextViewLayerAnchor.Caret,
        _ => TextViewLayerAnchor.Text
    };

    /// <summary>Raised before the visible lines are built, carrying the first line number.</summary>
    public event Action<int>? VisualLineConstructionStarting
    {
        add => _constructionStarting += value;
        remove => _constructionStarting -= value;
    }

    /// <summary>Raised after the visible lines were built.</summary>
    public event Action? VisualLinesChanged
    {
        add => _linesChanged += value;
        remove => _linesChanged -= value;
    }

    /// <summary>Height of the whole document in view coordinates.</summary>
    public double DocumentHeight => Host.ExtentHeight;

    /// <summary>Height of a line holding one character, independent of content.</summary>
    public double DefaultLineHeight => Host.DefaultLineHeight;

    /// <summary>Baseline of a line holding one character.</summary>
    public double DefaultBaseline => Host.DefaultBaseline;

    /// <summary>
    /// Width of a wide space, the unit AvalonEdit sizes gutters and column rulers in. Measured here
    /// rather than taken from the core, whose tab stops are defined on the space advance.
    /// </summary>
    public double WideSpaceWidth
    {
        get
        {
            var factory = Application.IsRunning
                ? Application.Current.GraphicsFactory
                : Application.DefaultGraphicsFactory;
            var layout = factory.TextEngine.GetOrCreateLayout(
                new TextLayoutRequest
                {
                    Text = "x".AsMemory(),
                    // Measured at the density the view lays out at, or every width derived from
                    // this one lands on the wrong column above 100% scaling.
                    Dpi = textArea.Editor.EditorDpi,
                    DefaultStyle = new TextRunStyle(FontFamily, textArea.Editor.FontSize, textArea.Editor.FontWeight),
                    Paragraph = new TextParagraphStyle
                    {
                        Wrapping = TextWrapping.NoWrap,
                        MaxWidth = double.PositiveInfinity
                    }
                },
                TextLayoutCachePolicy.Content);
            return layout.MeasuredSize.Width;
        }
    }

    /// <summary>Document-space top of a one-based document line.</summary>
    public double GetVisualTopByDocumentLine(int documentLineNumber)
        => Host.GetLineY(documentLineNumber - 1);

    /// <summary>One-based document line whose row contains the document-space Y.</summary>
    public int GetDocumentLineByVisualTop(double documentY)
        => Host.FindLineByY(documentY) + 1;

    /// <summary>The laid-out line containing the document-space Y, or null when not visible.</summary>
    public VisualLine? GetVisualLineFromVisualTop(double documentY)
    {
        foreach (var line in Host.VisibleTextLines)
        {
            if (documentY >= line.DocumentY && documentY < line.DocumentY + line.Height)
            {
                return Wrap(line);
            }
        }
        return null;
    }

    /// <summary>Document offset at a view-relative point, or null when the point misses the text.</summary>
    public int? GetPosition(Point viewPosition)
    {
        var hit = HitTest(viewPosition, out bool insideLine);
        return insideLine ? hit : null;
    }

    /// <summary>Document offset at a view-relative point, clamped to the nearest line.</summary>
    public int GetPositionFloor(Point viewPosition) => HitTest(viewPosition, out _);

    /// <summary>View-relative position of a document offset, in the same space <see cref="GetPosition"/> takes.</summary>
    public Point GetVisualPosition(int documentOffset)
    {
        var rect = Surface.GetCharRectInWindow(documentOffset);
        var viewport = Host.TextViewportBounds;
        return new Point(rect.X - viewport.X, rect.Y - viewport.Y);
    }

    public double HorizontalOffset => Host.ScrollOffset.X;

    public double VerticalOffset => Host.ScrollOffset.Y;

    public Point ScrollOffset => Host.ScrollOffset;

    /// <summary>Raised after the scroll offset changed.</summary>
    public event Action? ScrollOffsetChanged
    {
        add => _scrollOffsetChanged += value;
        remove => _scrollOffsetChanged -= value;
    }

    /// <summary>Scrolls the smallest amount that brings the document-space rectangle into view.</summary>
    public void MakeVisible(Rect documentRect) => Host.MakeVisible(documentRect);

    private int HitTest(Point viewPosition, out bool insideLine)
    {
        var viewport = Host.TextViewportBounds;
        double documentX = viewPosition.X + Host.ScrollOffset.X;
        double documentY = viewPosition.Y + Host.ScrollOffset.Y;
        insideLine = false;
        var lines = Host.VisibleTextLines;
        if (lines.Count == 0)
        {
            return 0;
        }
        foreach (var line in lines)
        {
            if (documentY < line.DocumentY || documentY >= line.DocumentY + line.Height)
            {
                continue;
            }
            var hit = line.HitTest(new Point(documentX - line.DocumentX, documentY - line.DocumentY));
            insideLine = documentX >= line.DocumentX && documentX <= line.DocumentX + viewport.Width;
            return line.LogicalLine.Offset + line.MapProjectedOffsetToSource(hit.InsertionIndex);
        }
        var nearest = documentY < lines[0].DocumentY ? lines[0] : lines[^1];
        return nearest.LogicalLine.Offset;
    }

    /// <summary>
    /// Lines currently laid out, in document order. Rebuilt from the engine's materialized lines on
    /// each read, so hold one only within a single pass over the view.
    /// </summary>
    public IReadOnlyList<VisualLine> VisualLines
    {
        get
        {
            var host = Host;
            var lines = host.VisibleTextLines;
            var result = new VisualLine[lines.Count];
            for (int index = 0; index < lines.Count; index++)
            {
                result[index] = Wrap(lines[index]);
            }
            return result;
        }
    }

    /// <summary>The laid-out line containing the document line number, or null when not visible.</summary>
    public VisualLine? GetVisualLine(int documentLineNumber)
    {
        foreach (var line in Host.VisibleTextLines)
        {
            if (line.LogicalLine.LineNumber == documentLineNumber - 1)
            {
                return Wrap(line);
            }
        }
        return null;
    }

    private VisualLine Wrap(TextLineLayout line)
        => new(
            line,
            Document.GetLineByOffset(line.LogicalLine.Offset),
            textArea.Editor.ElementGeneratorAdapter.GetScannedElements(line.LogicalLine.Offset));

    internal MultiLineTextBox Surface => textArea.Editor.Surface;
}
