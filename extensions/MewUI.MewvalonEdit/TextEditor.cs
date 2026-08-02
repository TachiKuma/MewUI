using System.ComponentModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.MewvalonEdit.Rendering;

namespace Aprillz.MewUI.MewvalonEdit;

public class TextEditor : ContentControl
{
    private TextDocument _document;
    private MultiLineTextBox _surface = null!;
    private LineNumberMargin _lineNumberMargin = null!;
    private IHighlightingDefinition? _syntaxHighlighting;
    private HighlightingColorizer? _colorizer;
    private readonly SpaceMarkerProjection _spaceMarkers;
    private readonly WhitespaceAdornmentProvider _whitespaceAdornments;
    private bool _showLineNumbers;

    public TextEditor()
    {
        Options = new TextEditorOptions();
        IndentationStrategy = new DefaultIndentationStrategy();
        _spaceMarkers = new SpaceMarkerProjection(Options);
        _whitespaceAdornments = new WhitespaceAdornmentProvider(Options, this);
        Options.PropertyChanged += OnOptionsChanged;
        _document = new TextDocument();
        _document.Changed += OnDocumentTextChanged;
        StyleSheet = new StyleSheet();
        StyleSheet.Define<TextEditor>(CreateFrameStyle());
        BuildSurface();
        TextArea = new TextArea(this);
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
            BuildSurface();
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
    internal event Action<MultiLineTextBox, MultiLineTextBox>? SurfaceChanged;

    public event EventHandler? TextChanged;
    public event EventHandler? DocumentChanged;

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

    private void BuildSurface()
    {
        var previous = _surface;
        if (previous is not null)
        {
            previous.TextInput -= OnSurfaceTextInput;
        }

        // The editor owns the frame so it encloses the line number margin, as AvalonEdit's
        // templated ScrollViewer encloses TextArea's left margins. The surface paints neither
        // border nor background: a square fill would cover the frame's rounded corners from the
        // inside. Font properties are inherited, so the surface must not take local values.
        _surface = new MultiLineTextBox(_document.CoreDocument)
        {
            Wrap = previous?.Wrap ?? false,
            IsReadOnly = previous?.IsReadOnly ?? false,
            AcceptTab = true,
            Background = Color.Transparent,
            BorderThickness = 0,
            CornerRadius = 0
        };
        _surface.TextInput += OnSurfaceTextInput;
        _surface.Extensions.Projections.Add(_spaceMarkers);
        _surface.Extensions.AdornmentProviders.Add(_whitespaceAdornments);
        _lineNumberMargin = new LineNumberMargin(this)
        {
            IsVisible = _showLineNumbers
        };
        ApplyHighlighting();
        Content = new Grid()
            .Columns("Auto,*")
            .Children(
                _surface.Column(1),
                _lineNumberMargin.Column(0));

        if (previous is not null)
        {
            SurfaceChanged?.Invoke(previous, _surface);
            previous.Dispose();
        }
    }

    private void ApplyHighlighting()
    {
        if (_surface is null) return;
        if (_colorizer is not null)
        {
            _surface.Extensions.Classifiers.Remove(_colorizer);
            _colorizer = null;
        }
        if (_syntaxHighlighting is not null)
        {
            _colorizer = new HighlightingColorizer(_syntaxHighlighting);
            _surface.Extensions.Classifiers.Add(_colorizer);
        }
        _surface.InvalidateTextView();
    }

    private void OnDocumentTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        _lineNumberMargin?.SyncWidthToLineCount();
        TextChanged?.Invoke(this, EventArgs.Empty);
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
        _surface.InvalidateTextView();
    }
}
