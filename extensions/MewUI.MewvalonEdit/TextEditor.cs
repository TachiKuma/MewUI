using System.ComponentModel;
using System.Text;
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

public class TextEditor : Control
{
    private const string PART_MARGIN_HOST = "PART_MarginHost";

    private readonly MultiLineTextBox _surface;
    private readonly LineNumberMargin _lineNumberMargin;
    private readonly System.Collections.ObjectModel.ObservableCollection<AbstractMargin> _leftMargins = [];
    private Grid? _marginHost;
    private HighlightingColorizer? _colorizer;
    private readonly SpaceMarkerProjection _spaceMarkers;
    private readonly SpaceMarkerClassifier _spaceMarkerColors;
    private readonly WhitespaceMarkerLayer _whitespaceMarkers;
    private readonly LineTransformerAdapter _lineTransformers;
    private readonly ElementGeneratorAdapter _elementGenerators;
    private readonly BackgroundRendererRegistry _backgroundRenderers;

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
        var document = new TextDocument();
        StyleSheet = new StyleSheet();
        StyleSheet.Define<TextEditor>(CreateFrameStyle());

        // The editor owns the frame so it encloses the line number margin, as AvalonEdit's
        // templated ScrollViewer encloses TextArea's left margins. The surface paints neither
        // border nor background: a square fill would cover the frame's rounded corners from the
        // inside. Font properties are inherited, so the surface must not take local values.
        _surface = new MultiLineTextBox(document.CoreDocument)
        {
            Wrap = false,
            AcceptTab = true,
            TabSize = Options.IndentationSize,
            Background = Color.Transparent,
            BorderThickness = 0,
            CornerRadius = 0
        };
        _surface.KeyDown += OnSurfaceKeyDown;
        _surface.TextCommitted += OnTextCommitted;
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
        // After the transformers so a link underline survives the colorizer's colours.
        _surface.Extensions.Classifiers.Add(_elementGenerators);
        _surface.Extensions.Transformers.Add(_lineTransformers);
        _surface.Extensions.Classifiers.Add(_spaceMarkerColors);
        _surface.Extensions.ElementGenerators.Add(_elementGenerators);
        _backgroundRenderers.RegisterInto(_surface);
        _surface.InsertLayer(_whitespaceMarkers, TextViewLayerAnchor.Text, TextLayerPosition.Below);
        _surface.InsertLayer(
            new CurrentLineLayer(Options, this), TextViewLayerAnchor.Background, TextLayerPosition.Above);
        _surface.InsertLayer(
            new ColumnRulerLayer(Options, this), TextViewLayerAnchor.Text, TextLayerPosition.Below);
        _lineNumberMargin = new LineNumberMargin { IsVisible = ShowLineNumbers };
        _lineNumberMargin.WithTheme((theme, margin) =>
            margin.Foreground = LineNumbersForeground ?? theme.Palette.PlaceholderText);
        // Assigned once the surface and the margin exist, because the change callback wires both.
        Document = document;
        Template = new DelegateControlTemplate<TextEditor>(BuildTemplate);
        TextArea = new TextArea(this);
        _leftMargins.CollectionChanged += (_, _) => OnLeftMarginsChanged();
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

    private static Element BuildTemplate(TextEditor owner, ControlTemplateContext context)
    {
        var host = new Grid();
        context.Register(PART_MARGIN_HOST, host);
        // A templated control suppresses its own chrome, so the border has to draw it.
        var chrome = new Border { Child = host, ClipToBounds = true };
        context.BindChrome(chrome);
        return chrome;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _marginHost = GetTemplateChild<Grid>(PART_MARGIN_HOST);
        OnLeftMarginsChanged();
    }

    private void OnLeftMarginsChanged()
    {
        // Attachment stays here rather than in the template, so a margin is connected the moment it
        // joins the collection whether or not a layout pass has run.
        foreach (var margin in _leftMargins)
        {
            margin.TextView = TextArea.TextView;
        }
        RebuildMargins();
    }

    /// <summary>Lays the margins out as leading grid columns, outermost first.</summary>
    private void RebuildMargins()
    {
        if (_marginHost is not Grid host)
        {
            return;
        }

        host.Clear();
        host.Columns(string.Join(',', Enumerable.Repeat("Auto", _leftMargins.Count).Append("*")));
        for (int index = 0; index < _leftMargins.Count; index++)
        {
            var margin = _leftMargins[index];
            host.Children(margin);
            // After the add: adding re-parents the child and a column set before that is lost.
            margin.Column(index);
        }
        host.Children(_surface);
        _surface.Column(_leftMargins.Count);
    }

    public static readonly MewProperty<TextDocument?> DocumentProperty =
        MewProperty<TextDocument?>.Register<TextEditor>(nameof(Document), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnDocumentPropertyChanged(oldValue, newValue),
            validate: static (_, value) => ArgumentNullException.ThrowIfNull(value));

    /// <summary>Document being edited. Never null; the editor creates one for itself.</summary>
    public TextDocument Document
    {
        get => GetValue(DocumentProperty)!;
        set => SetValue(DocumentProperty, value);
    }

    private void OnDocumentPropertyChanged(TextDocument? oldValue, TextDocument? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Changed -= OnDocumentTextChanged;
            oldValue.Surface = null;
        }
        if (newValue is null)
        {
            return;
        }

        newValue.Changed += OnDocumentTextChanged;
        newValue.Surface = _surface;
        _surface.Document = newValue.CoreDocument;
        _lineNumberMargin.SyncWidthToLineCount();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    public TextEditorOptions Options { get; }
    public TextArea TextArea { get; }
    public IIndentationStrategy? IndentationStrategy { get; set; }

    /// <summary>
    /// The document text. Assigning it starts over: the caret returns to the beginning and the undo
    /// history is dropped, so the text that was there cannot be brought back.
    /// </summary>
    public string Text
    {
        get => Document.Text;
        set
        {
            // Through the surface, whose own setter drops the history the way the original's
            // UndoStack.ClearAll does; Document.Text alone would leave the replace undoable.
            _surface.Text = value ?? string.Empty;
            CaretOffset = 0;
        }
    }

    public static readonly MewProperty<IHighlightingDefinition?> SyntaxHighlightingProperty =
        MewProperty<IHighlightingDefinition?>.Register<TextEditor>(nameof(SyntaxHighlighting), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ApplyHighlighting());

    public IHighlightingDefinition? SyntaxHighlighting
    {
        get => GetValue(SyntaxHighlightingProperty);
        set => SetValue(SyntaxHighlightingProperty, value);
    }

    public static readonly MewProperty<bool> WordWrapProperty =
        MewProperty<bool>.Register<TextEditor>(nameof(WordWrap), false,
            MewPropertyOptions.AffectsLayout,
            static (self, _, newValue) => self._surface.Wrap = newValue);

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public static readonly MewProperty<bool> IsReadOnlyProperty =
        MewProperty<bool>.Register<TextEditor>(nameof(IsReadOnly), false,
            MewPropertyOptions.None,
            static (self, _, newValue) => self._surface.IsReadOnly = newValue);

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly MewProperty<bool> ShowLineNumbersProperty =
        MewProperty<bool>.Register<TextEditor>(nameof(ShowLineNumbers), false,
            MewPropertyOptions.AffectsLayout,
            static (self, _, newValue) => self._lineNumberMargin.IsVisible = newValue);

    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public static readonly MewProperty<Color?> LineNumbersForegroundProperty =
        MewProperty<Color?>.Register<TextEditor>(nameof(LineNumbersForeground), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, newValue) => self.ApplyLineNumbersForeground(newValue));

    /// <summary>Colour of the line numbers. Null follows the theme.</summary>
    public Color? LineNumbersForeground
    {
        get => GetValue(LineNumbersForegroundProperty);
        set => SetValue(LineNumbersForegroundProperty, value);
    }

    private void ApplyLineNumbersForeground(Color? value)
    {
        // A local value, or the inherited Foreground would hand the numbers the body text colour.
        _lineNumberMargin.Foreground = value ?? Theme.Palette.PlaceholderText;
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

    /// <summary>Pixel density the text is laid out at. Generated elements measure at the same one.</summary>
    internal uint EditorDpi => GetDpi();
    internal Color WhitespaceMarkerColor => TextArea.TextView.ResolvedNonPrintableCharacter;

    internal Color PlaceholderColor => Theme.Palette.PlaceholderText;

    internal Color AccentColor => Theme.Palette.Accent;

    internal Color ControlBorderColor => Theme.Palette.ControlBorder;

    internal bool ThemeIsDark => Theme.IsDark;
    internal Color ThemeSelectionBackground => Theme.Palette.SelectionBackground;
    internal Color FoldingMarkerColor => Theme.Palette.PlaceholderText;
    internal ElementGeneratorAdapter ElementGeneratorAdapter => _elementGenerators;
    internal IList<IBackgroundRenderer> BackgroundRenderers => _backgroundRenderers.Renderers;
    internal IList<IVisualLineTransformer> LineTransformers => _lineTransformers.Transformers;
    internal IList<VisualLineElementGenerator> ElementGenerators => _elementGenerators.Generators;

    public event EventHandler? TextChanged;
    public event EventHandler? DocumentChanged;

    public static readonly MewProperty<System.Text.Encoding?> EncodingProperty =
        MewProperty<System.Text.Encoding?>.Register<TextEditor>(nameof(Encoding), null);

    /// <summary>Encoding used by <see cref="Save(Stream)"/>. <see cref="Load(Stream)"/> stores what it detected.</summary>
    public System.Text.Encoding? Encoding
    {
        get => GetValue(EncodingProperty);
        set => SetValue(EncodingProperty, value);
    }

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
        Document.Changed -= OnDocumentTextChanged;
        base.OnDispose();
    }

    private void ApplyHighlighting()
    {
        if (_colorizer is not null)
        {
            LineTransformers.Remove(_colorizer);
            _colorizer = null;
        }
        if (SyntaxHighlighting is IHighlightingDefinition definition)
        {
            // First in the list: syntax colors are the base layer, so whitespace markers and search
            // highlights registered later keep their own colors where the ranges overlap.
            _colorizer = new HighlightingColorizer(definition);
            _colorizer.HighlightingStateChanged += RepaintHighlightedLines;
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
    /// Rebuilds the lines a highlighting span changed the starting state of. The signal arrives
    /// while a line is being laid out; the surface absorbs that and rebuilds once the pass ends.
    /// </summary>
    private void RepaintHighlightedLines(int fromLineNumber, int toLineNumber)
    {
        var document = Document;
        int first = Math.Clamp(fromLineNumber, 1, document.LineCount);
        int last = Math.Clamp(toLineNumber, first, document.LineCount);
        int start = document.GetLineByNumber(first).Offset;
        var lastLine = document.GetLineByNumber(last);
        _surface.InvalidateTextRange(start, lastLine.Offset + lastLine.TotalLength - start);
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

    /// <summary>
    /// Claims Enter so the inserted terminator matches the one already in use around the caret.
    /// The surface would insert a line feed, which turns a CRLF file into a mixed one.
    /// </summary>
    private void OnSurfaceKeyDown(KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Enter || IsReadOnly || !Document.CoreDocument.PreservesLineEndings)
        {
            return;
        }
        string newLine = TextUtilities.GetNewLineFromDocument(Document, Document.GetLocation(CaretOffset).Line);
        if (newLine == "\n")
        {
            return;
        }
        e.Handled = true;
        _surface.ReplaceSelection(newLine);
    }

    /// <summary>
    /// Indents the line the caret landed on after a line break, which is where the original applies
    /// the strategy. A line that is not fully editable is left alone, as there too.
    /// </summary>
    private void OnTextCommitted(string text)
    {
        if (IndentationStrategy is null || !TextUtilities.IsNewLine(text))
        {
            return;
        }
        var line = Document.GetLineByNumber(Document.GetLocation(CaretOffset).Line);
        if (!IsFullyEditable(line))
        {
            return;
        }
        IndentationStrategy.IndentLine(Document, line);
    }

    private bool IsFullyEditable(DocumentLine line)
    {
        var provider = TextArea.ReadOnlySectionProvider;
        if (provider is null)
        {
            return true;
        }
        var deletable = provider.GetDeletableSegments(new SimpleSegment(line.Offset, line.Length)).ToArray();
        return deletable.Length == 1
            && deletable[0].Offset == line.Offset
            && deletable[0].Length == line.Length;
    }

    private void OnSurfaceTextInput(TextInputEventArgs e)
    {
        if (!Options.ConvertTabsToSpaces || string.IsNullOrEmpty(e.Text) || !e.Text.Contains('\t'))
        {
            return;
        }
        e.Handled = true;
        InsertTextInput(e.Text);
    }

    /// <summary>
    /// Inserts text as if typed. Both the keyboard path and the programmatic one come through here,
    /// so a tab converts the same way whichever put it in.
    /// </summary>
    internal void InsertTextInput(string text)
        => _surface.ReplaceSelection(
            Options.ConvertTabsToSpaces && text.Contains('\t')
                ? ExpandTabs(text, Document.GetLocation(SelectionStart).Column)
                : text);

    /// <summary>
    /// Replaces every tab with spaces reaching the next indentation stop, starting from the column
    /// the text is going into. A whole indent per tab would overshoot every stop but the first.
    /// </summary>
    private string ExpandTabs(string text, int column)
    {
        var expanded = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (character == '\t')
            {
                string spaces = Options.GetIndentationString(column);
                expanded.Append(spaces);
                column += spaces.Length;
            }
            else
            {
                expanded.Append(character);
                column = character is '\n' or '\r' ? 1 : column + 1;
            }
        }
        return expanded.ToString();
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        _surface.TabSize = Options.IndentationSize;
        _surface.InvalidateTextView();
    }
}
