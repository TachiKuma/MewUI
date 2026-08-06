using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

public sealed class TextArea : MewObject, ITextEditorComponent
{
    private readonly TextEditor _editor;
    private SelectionLayer? _selectionLayer;
    private Selection _selection;
    private bool _applyingSelection;

    internal TextArea(TextEditor editor)
    {
        _editor = editor;
        Caret = new Caret(this);
        EmptySelection = new EmptySelection(this);
        _selection = EmptySelection;
        TextView = new TextView(this);
        TextView.Services.AddService(this);
        // Taken unconditionally: the caret's colour, its visibility and its overstrike width all
        // live here, and a caret that only sometimes belongs to the editor would answer differently
        // depending on whether one of them had been touched.
        TextView.InsertLayer(new CaretLayer(this), KnownLayer.Caret, LayerInsertionPosition.Replace);
        editor.Surface.EditingStateChanged += OnEditingStateChanged;
        editor.Surface.TextInput += OnTextInput;
    }

    public TextDocument Document => _editor.Document;
    public TextEditorOptions Options => _editor.Options;
    public Caret Caret { get; }
    public TextView TextView { get; }

    /// <summary>The one empty selection of this text area, which is what an empty selection is.</summary>
    internal Selection EmptySelection { get; }

    /// <summary>
    /// What is selected. Assigning moves the editing surface, and a selection the surface makes on
    /// its own replaces this with the matching range.
    /// </summary>
    /// <remarks>
    /// The surface keeps a start and a length, with no direction, so a selection it originates comes
    /// back reading forwards even if it was dragged backwards. A selection assigned here keeps its
    /// direction until the surface changes it.
    /// </remarks>
    public Selection Selection
    {
        get => _selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_selection.Equals(value))
            {
                return;
            }
            _selection = value;
            ApplyToSurface(value);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyToSurface(Selection selection)
    {
        _applyingSelection = true;
        try
        {
            if (selection.SurroundingSegment is ISegment segment)
            {
                _editor.Select(segment.Offset, segment.Length);
            }
            else
            {
                _editor.Select(_editor.CaretOffset, 0);
            }
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    /// <summary>
    /// Margins placed left of the text, outermost first. Adding one attaches it to the view; the
    /// line number margin is the built-in entry that <see cref="TextEditor.ShowLineNumbers"/> adds
    /// and removes.
    /// </summary>
    public IList<AbstractMargin> LeftMargins => _editor.LeftMargins;

    /// <summary>The requested service, or null when neither the view nor the document has it.</summary>
    public TService? GetService<TService>() where TService : class => TextView.GetService<TService>();

    public IIndentationStrategy? IndentationStrategy
    {
        get => _editor.IndentationStrategy;
        set => _editor.IndentationStrategy = value;
    }

    public event EventHandler? SelectionChanged;
    public event Action<TextInputEventArgs>? TextEntering;

    /// <summary>
    /// Raised after typed or composed text reached the document, once per commit. During an IME
    /// composition only the final commit raises it, which makes it the completion trigger point.
    /// </summary>
    public event Action<string>? TextEntered
    {
        add => _editor.Surface.TextCommitted += value;
        remove => _editor.Surface.TextCommitted -= value;
    }

    public static readonly MewProperty<Color?> SelectionBrushProperty =
        MewProperty<Color?>.Register<TextArea>(nameof(SelectionBrush), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, newValue) =>
            {
                var layer = self.ResolveSelectionLayer(newValue.HasValue);
                if (layer is not null)
                {
                    layer.Background = newValue;
                }
            });

    /// <summary>Background painted behind the selection. Null restores the theme's selection.</summary>
    public Color? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Color the selected glyphs are painted in. Null leaves them as they are, matching the
    /// original, where a null brush makes the selection colorizer keep the existing foreground.
    /// </summary>
    public Color? SelectionForeground
    {
        get => _editor.Surface.SelectionForeground;
        set => _editor.Surface.SelectionForeground = value;
    }

    public static readonly MewProperty<Color?> SelectionBorderProperty =
        MewProperty<Color?>.Register<TextArea>(nameof(SelectionBorder), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, newValue) =>
            {
                var layer = self.ResolveSelectionLayer(newValue.HasValue);
                if (layer is not null)
                {
                    layer.Border = newValue;
                }
            });

    /// <summary>Outline drawn around the selection. Null draws none.</summary>
    public Color? SelectionBorder
    {
        get => GetValue(SelectionBorderProperty);
        set => SetValue(SelectionBorderProperty, value);
    }

    public static readonly MewProperty<bool> OverstrikeModeProperty =
        MewProperty<bool>.Register<TextArea>(nameof(OverstrikeMode), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Whether typing overwrites the character at the caret rather than inserting before it. The
    /// caret covers that character while it is on, translucently, so the character stays readable.
    /// </summary>
    public bool OverstrikeMode
    {
        get => GetValue(OverstrikeModeProperty);
        set => SetValue(OverstrikeModeProperty, value);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        // No visual tree here, so AffectsRender invalidates nothing by itself.
        if (property.AffectsRender)
        {
            _editor.InvalidateTextView();
        }
    }

    /// <summary>
    /// The replacement selection layer, installed on the first appearance change. Replacing the
    /// anchor stops the host painting its own selection, so clearing a property back to its default
    /// must not install one; an editor that only ever clears keeps the theme's selection untouched.
    /// </summary>
    private SelectionLayer? ResolveSelectionLayer(bool install)
    {
        if (_selectionLayer is null && install)
        {
            _selectionLayer = new SelectionLayer(this);
            TextView.InsertLayer(_selectionLayer, KnownLayer.Selection, LayerInsertionPosition.Replace);
        }
        return _selectionLayer;
    }

    /// <summary>Consulted before every edit. Null leaves the document fully editable.</summary>
    public IReadOnlySectionProvider? ReadOnlySectionProvider
    {
        get => (_editor.Surface.EditableRegions as ReadOnlySectionAdapter)?.Provider;
        set => _editor.Surface.EditableRegions = value is null ? null : new ReadOnlySectionAdapter(value);
    }

    /// <summary>Raised after the document was replaced.</summary>
    public event EventHandler? DocumentChanged
    {
        add => _editor.DocumentChanged += value;
        remove => _editor.DocumentChanged -= value;
    }

    /// <summary>Raised after an option changed.</summary>
    public event EventHandler<MewProperty>? OptionChanged
    {
        add => Options.OptionChanged += value;
        remove => Options.OptionChanged -= value;
    }

    public void ReplaceSelection(string? text) => _editor.Surface.ReplaceSelection(text);

    /// <summary>Inserts text as if typed, replacing the selection.</summary>
    public void PerformTextInput(string text) => _editor.InsertTextInput(text ?? string.Empty);

    /// <summary>Collapses the selection to the caret.</summary>
    public void ClearSelection() => _editor.Select(_editor.CaretOffset, 0);

    private void OnEditingStateChanged()
    {
        Caret.RaisePositionChanged();
        if (!_applyingSelection)
        {
            int start = _editor.SelectionStart;
            _selection = Selection.Create(this, start, start + _editor.SelectionLength);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextInput(TextInputEventArgs args) => TextEntering?.Invoke(args);

    internal TextEditor Editor => _editor;
}

public sealed class Caret(TextArea textArea)
{
    // Room kept between the caret and the edge of the view while scrolling it into sight.
    internal const double MINIMUM_DISTANCE_TO_VIEW_BORDER = 30;

    private Color? _caretBrush;
    private bool _isVisible = true;

    public int Offset
    {
        get => textArea.Editor.CaretOffset;
        set => textArea.Editor.CaretOffset = value;
    }

    public int Line => textArea.Document.GetLocation(Offset).Line;
    public int Column => textArea.Document.GetLocation(Offset).Column;
    public TextLocation Location => textArea.Document.GetLocation(Offset);

    /// <summary>
    /// Where the caret is, including the visual column it lands on. Assigning takes the location
    /// and leaves the visual column to be worked out from it.
    /// </summary>
    public TextViewPosition Position
    {
        get => new(Location, VisualColumn);
        set => Offset = textArea.Document.GetOffset(value.Line, value.Column);
    }

    /// <summary>Visual column of the caret, which a projection moves away from the column.</summary>
    public int VisualColumn
    {
        get
        {
            var line = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(Offset));
            return line is null ? Column - 1 : line.GetVisualColumn(Offset - line.StartOffset);
        }
    }

    /// <summary>Colour of the caret. Null follows the editor's foreground.</summary>
    public Color? CaretBrush
    {
        get => _caretBrush;
        set
        {
            if (_caretBrush == value) return;
            _caretBrush = value;
            textArea.Editor.Surface.InvalidateLayer(Aprillz.MewUI.Text.TextViewLayerAnchor.Caret);
        }
    }

    /// <summary>Whether the caret is drawn at all, apart from the blink it follows while shown.</summary>
    public bool IsVisible => _isVisible;

    /// <summary>Draws the caret again after a <see cref="Hide"/>.</summary>
    public void Show() => SetVisible(true);

    /// <summary>Stops drawing the caret until <see cref="Show"/>, as a drag over the text does.</summary>
    public void Hide() => SetVisible(false);

    /// <summary>Scrolls the smallest amount that brings the caret into view.</summary>
    public void BringCaretToView() => textArea.Editor.Surface.ScrollToCaret();

    public event EventHandler? PositionChanged;

    internal void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);

    private void SetVisible(bool value)
    {
        if (_isVisible == value) return;
        _isVisible = value;
        textArea.Editor.Surface.InvalidateLayer(Aprillz.MewUI.Text.TextViewLayerAnchor.Caret);
    }
}
