using System.ComponentModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit;

public class TextEditor : ContentControl
{
    private TextDocument _document;
    private readonly MultiLineTextBox _surface;
    private readonly LineNumberMargin _lineNumberMargin;
    private readonly System.Collections.ObjectModel.ObservableCollection<AbstractMargin> _leftMargins = [];
    private Grid _marginHost = null!;
    private IHighlightingDefinition? _syntaxHighlighting;
    private HighlightingColorizer? _colorizer;
    private readonly SpaceMarkerProjection _spaceMarkers;
    private readonly SpaceMarkerClassifier _spaceMarkerColors;
    private readonly WhitespaceMarkerLayer _whitespaceMarkers;
    private readonly LineTransformerAdapter _lineTransformers;
    private readonly ElementGeneratorAdapter _elementGenerators;
    private readonly BackgroundRendererRegistry _backgroundRenderers;
    private bool _showLineNumbers;
    private bool _highlightingRefreshPending;

    public TextEditor()
    {
        Options = new TextEditorOptions();
        IndentationStrategy = new DefaultIndentationStrategy();
        _spaceMarkers = new SpaceMarkerProjection(Options);
        _spaceMarkerColors = new SpaceMarkerClassifier(Options, this);
        _whitespaceMarkers = new WhitespaceMarkerLayer(Options, this);
        _lineTransformers = new LineTransformerAdapter(this);
        _elementGenerators = new ElementGeneratorAdapter(this);
        _backgroundRenderers = new BackgroundRendererRegistry(this);
        Options.PropertyChanged += OnOptionsChanged;
        _document = new TextDocument();
        _document.Changed += OnDocumentTextChanged;
        StyleSheet = new StyleSheet();
        StyleSheet.Define<TextEditor>(CreateFrameStyle());

        // The editor owns the frame so it encloses the line number margin, as AvalonEdit's
        // templated ScrollViewer encloses TextArea's left margins. The surface paints neither
        // border nor background: a square fill would cover the frame's rounded corners from the
        // inside. Font properties are inherited, so the surface must not take local values.
        _surface = new MultiLineTextBox(_document.CoreDocument)
        {
            Wrap = false,
            AcceptTab = true,
            TabSize = Options.IndentationSize,
            Background = Color.Transparent,
            BorderThickness = 0,
            CornerRadius = 0
        };
        _surface.TextInput += OnSurfaceTextInput;
        _surface.MouseDown += OnSurfaceMouseDown;
        _surface.MouseMove += OnSurfaceMouseMove;
        // The generator projection runs first so it scans raw document text; the space markers
        // then restyle whatever survives, including projected replacement text.
        _surface.Extensions.Projections.Add(_elementGenerators);
        _surface.Extensions.Projections.Add(_spaceMarkers);
        // Ported transformers land below the whitespace markers, as AvalonEdit's baked marker
        // glyphs cannot be recolored by a colorizer.
        _surface.Extensions.Classifiers.Add(_lineTransformers);
        _surface.Extensions.Transformers.Add(_lineTransformers);
        _surface.Extensions.Classifiers.Add(_spaceMarkerColors);
        _surface.Extensions.ElementGenerators.Add(_elementGenerators);
        _backgroundRenderers.RegisterInto(_surface);
        _surface.InsertLayer(_whitespaceMarkers, TextViewLayerAnchor.Text, TextLayerPosition.Below);
        _lineNumberMargin = new LineNumberMargin { IsVisible = _showLineNumbers };
        _marginHost = new Grid().Columns("Auto,*").Children(_surface.Column(1));
        Content = _marginHost;
        TextArea = new TextArea(this);
        _leftMargins.CollectionChanged += (_, _) => RebuildMargins();
        _leftMargins.Add(_lineNumberMargin);
    }

    /// <summary>
    /// Frames the editor like the built-in text inputs. The style lives on the editor's own
    /// StyleSheet because default styles are registered for core control types only; hover and
    /// focus resolve from IsFocusWithin, so the frame reacts while the inner surface holds focus.
    /// </summary>
    private static Style CreateFrameStyle() =>
        new(typeof(TextEditor))
        {
            Transitions =
            [
                Transition.Create(BackgroundProperty),
                Transition.Create(BorderBrushProperty),
            ],
            Setters =
            [
                Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground),
                Setter.Create(BorderBrushProperty, theme => theme.Palette.ControlBorder),
                Setter.Create(BorderThicknessProperty, theme => theme.Metrics.ControlBorderThickness),
                Setter.Create(CornerRadiusProperty, theme => theme.Metrics.ControlCornerRadius),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(BorderBrushProperty,
                            theme => Color.Composite(theme.Palette.ControlBorder, theme.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(BorderBrushProperty, theme => theme.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(BackgroundProperty, theme => theme.Palette.DisabledControlBackground),
                        Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText),
                    ],
                },
            ],
        };

    internal IList<AbstractMargin> LeftMargins => _leftMargins;

    /// <summary>
    /// Lays the margins out as leading grid columns, outermost first, and attaches each to the view
    /// so it follows scrolling and line construction.
    /// </summary>
    private void RebuildMargins()
    {
        var columns = string.Join(',', Enumerable.Repeat("Auto", _leftMargins.Count).Append("*"));
        var host = new Grid().Columns(columns);
        for (int index = 0; index < _leftMargins.Count; index++)
        {
            var margin = _leftMargins[index];
            margin.TextView = TextArea.TextView;
            host.Children(margin);
            // Assigned after the add: adding transfers the child away from the previous host grid,
            // and the column set before that does not survive the move.
            margin.Column(index);
        }
        host.Children(_surface);
        _surface.Column(_leftMargins.Count);
        _marginHost = host;
        Content = host;
    }

    public TextDocument Document
    {
        get => _document;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_document, value)) return;
            _document.Changed -= OnDocumentTextChanged;
            _document = value;
            _document.Changed += OnDocumentTextChanged;
            _surface.Document = value.CoreDocument;
            _lineNumberMargin.SyncWidthToLineCount();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TextEditorOptions Options { get; }
    public TextArea TextArea { get; }
    public IIndentationStrategy? IndentationStrategy { get; set; }

    public string Text
    {
        get => Document.Text;
        set => Document.Text = value ?? string.Empty;
    }

    public IHighlightingDefinition? SyntaxHighlighting
    {
        get => _syntaxHighlighting;
        set
        {
            if (ReferenceEquals(_syntaxHighlighting, value)) return;
            _syntaxHighlighting = value;
            ApplyHighlighting();
        }
    }

    public bool WordWrap
    {
        get => _surface.Wrap;
        set => _surface.Wrap = value;
    }

    public bool IsReadOnly
    {
        get => _surface.IsReadOnly;
        set => _surface.IsReadOnly = value;
    }

    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers == value) return;
            _showLineNumbers = value;
            _lineNumberMargin.IsVisible = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Color LineNumbersForeground
    {
        get => _lineNumberMargin.NumberForeground;
        set
        {
            _lineNumberMargin.NumberForeground = value;
            _lineNumberMargin.InvalidateVisual();
        }
    }

    public int CaretOffset
    {
        get => _surface.CaretPosition;
        set => _surface.CaretPosition = value;
    }

    public int SelectionStart => _surface.SelectionStart;
    public int SelectionLength => _surface.SelectionLength;
    public string SelectedText => _surface.SelectedText;
    public int LineCount => Document.LineCount;
    public bool CanUndo => _surface.CanUndo;
    public bool CanRedo => _surface.CanRedo;
    public double VerticalOffset => _surface.VerticalOffset;
    public double HorizontalOffset => _surface.HorizontalOffset;

    internal MultiLineTextBox Surface => _surface;
    internal Color WhitespaceMarkerColor => Theme.Palette.PlaceholderText;
    internal Color ThemeSelectionBackground => Theme.Palette.SelectionBackground;
    internal Color FoldingMarkerColor => Theme.Palette.PlaceholderText;
    internal ElementGeneratorAdapter ElementGeneratorAdapter => _elementGenerators;
    internal IList<IBackgroundRenderer> BackgroundRenderers => _backgroundRenderers.Renderers;
    internal IList<IVisualLineTransformer> LineTransformers => _lineTransformers.Transformers;
    internal IList<VisualLineElementGenerator> ElementGenerators => _elementGenerators.Generators;

    public event EventHandler? TextChanged;
    public event EventHandler? DocumentChanged;

    /// <summary>Encoding used by <see cref="Save(Stream)"/>. <see cref="Load(Stream)"/> stores what it detected.</summary>
    public System.Text.Encoding? Encoding { get; set; }

    public void Load(string fileName)
    {
        using var stream = File.OpenRead(fileName);
        Load(stream);
    }

    public void Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        Text = reader.ReadToEnd();
        Encoding = reader.CurrentEncoding;
    }

    public void Save(string fileName)
    {
        using var stream = File.Create(fileName);
        Save(stream);
    }

    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var writer = new StreamWriter(stream, Encoding ?? new System.Text.UTF8Encoding(false), leaveOpen: true);
        writer.Write(Text);
        writer.Flush();
    }

    public void Select(int start, int length) => _surface.Select(start, length);
    public void SelectAll() => _surface.SelectAll();
    public void AppendText(string? text) => _surface.AppendText(text, scrollToCaret: true);
    public void Copy() => _surface.Copy();
    public void Cut() => _surface.Cut();
    public void Paste() => _surface.Paste();
    public void Undo() => _surface.Undo();
    public void Redo() => _surface.Redo();
    public void InvalidateTextView() => _surface.InvalidateTextView();

    protected override void OnDispose()
    {
        Options.PropertyChanged -= OnOptionsChanged;
        _document.Changed -= OnDocumentTextChanged;
        base.OnDispose();
    }

    private void ApplyHighlighting()
    {
        if (_colorizer is not null)
        {
            LineTransformers.Remove(_colorizer);
            _colorizer = null;
        }
        if (_syntaxHighlighting is not null)
        {
            // First in the list: syntax colors are the base layer, so whitespace markers and search
            // highlights registered later keep their own colors where the ranges overlap.
            _colorizer = new HighlightingColorizer(_syntaxHighlighting, () => Theme.IsDark);
            _colorizer.HighlightingStateChanged += (_, _) => RequestHighlightingRefresh();
            LineTransformers.Insert(0, _colorizer);
            // The highlighter is reachable from the view alone, as in AvalonEdit, so ported code
            // that only holds a TextView can still ask for the document's highlighting state.
            TextArea.TextView.Services.AddService<IHighlighter>(_colorizer.GetHighlighter(Document));
        }
        else
        {
            TextArea.TextView.Services.RemoveService(typeof(IHighlighter));
        }
        _surface.InvalidateTextView();
    }

    /// <summary>
    /// Repaints after a highlighting span changed the state the lines below start from. The signal
    /// arrives while a line is being laid out, so the rebuild is posted instead of run in place.
    /// </summary>
    private void RequestHighlightingRefresh()
    {
        if (_highlightingRefreshPending)
        {
            return;
        }
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher is null)
        {
            return;
        }
        _highlightingRefreshPending = true;
        dispatcher.BeginInvoke(() =>
        {
            _highlightingRefreshPending = false;
            InvalidateTextView();
        });
    }

    private void OnDocumentTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        _lineNumberMargin?.SyncWidthToLineCount();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSurfaceMouseDown(MouseEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        // Ahead of the surface's own caret placement: its OnMouseDown raises this event first and
        // honors Handled, which is the AvalonEdit "if (!e.Handled) route to element" structure.
        FindElementAtPoint(ToWindowPoint(e))?.OnMouseDown(e);
    }

    private void OnSurfaceMouseMove(MouseEventArgs e)
    {
        var position = ToWindowPoint(e);
        if (FindElementAtPoint(position) is not VisualLineElement element)
        {
            _surface.Cursor = CursorType.IBeam;
            return;
        }
        var query = new QueryCursorEventArgs(position, e.Modifiers);
        element.OnQueryCursor(query);
        _surface.Cursor = query.Cursor ?? CursorType.IBeam;
    }

    private Point ToWindowPoint(MouseEventArgs e)
    {
        var local = e.GetPosition(_surface);
        return new Point(local.X + _surface.Bounds.X, local.Y + _surface.Bounds.Y);
    }

    private VisualLineElement? FindElementAtPoint(Point position)
    {
        var viewport = _surface.TextViewportBounds;
        if (!viewport.Contains(position))
        {
            return null;
        }
        ITextViewHost host = _surface;
        double documentX = position.X - viewport.X + host.ScrollOffset.X;
        double documentY = position.Y - viewport.Y + host.ScrollOffset.Y;
        foreach (var line in host.VisibleTextLines)
        {
            if (documentY < line.DocumentY || documentY >= line.DocumentY + line.Height)
            {
                continue;
            }
            var hit = line.HitTest(new Point(documentX - line.DocumentX, documentY - line.DocumentY));
            int sourceOffset = line.MapProjectedOffsetToSource(hit.FirstCharacterIndex);
            return _elementGenerators.FindElementAt(line.LogicalLine.Offset + sourceOffset);
        }
        return null;
    }

    private void OnSurfaceTextInput(TextInputEventArgs e)
    {
        if (!Options.ConvertTabsToSpaces || string.IsNullOrEmpty(e.Text) || !e.Text.Contains('\t')) return;
        e.Handled = true;
        string replacement = e.Text.Replace("\t", new string(' ', Options.IndentationSize), StringComparison.Ordinal);
        _surface.ReplaceSelection(replacement);
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        _surface.AcceptTab = true;
        _surface.TabSize = Options.IndentationSize;
        _surface.InvalidateTextView();
    }
}
