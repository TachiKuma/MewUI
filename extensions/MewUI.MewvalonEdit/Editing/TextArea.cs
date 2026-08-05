using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

public sealed class TextArea : MewObject
{
    private readonly TextEditor _editor;
    private SelectionLayer? _selectionLayer;

    internal TextArea(TextEditor editor)
    {
        _editor = editor;
        Caret = new Caret(this);
        Selection = new Selection(this);
        TextView = new TextView(this);
        editor.Surface.EditingStateChanged += OnEditingStateChanged;
        editor.Surface.TextInput += OnTextInput;
    }

    public TextDocument Document => _editor.Document;
    public TextEditorOptions Options => _editor.Options;
    public Caret Caret { get; }
    public Selection Selection { get; }
    public TextView TextView { get; }

    /// <summary>
    /// Margins placed left of the text, outermost first. Adding one attaches it to the view; the
    /// line number margin is the built-in entry that <see cref="TextEditor.ShowLineNumbers"/> adds
    /// and removes.
    /// </summary>
    public IList<AbstractMargin> LeftMargins => _editor.LeftMargins;

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
    public event System.ComponentModel.PropertyChangedEventHandler? OptionChanged
    {
        add => Options.PropertyChanged += value;
        remove => Options.PropertyChanged -= value;
    }

    public void ReplaceSelection(string? text) => _editor.Surface.ReplaceSelection(text);

    /// <summary>Inserts text as if typed, replacing the selection.</summary>
    public void PerformTextInput(string text) => _editor.InsertTextInput(text ?? string.Empty);

    /// <summary>Collapses the selection to the caret.</summary>
    public void ClearSelection() => _editor.Select(_editor.CaretOffset, 0);

    private void OnEditingStateChanged()
    {
        Caret.RaisePositionChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextInput(TextInputEventArgs args) => TextEntering?.Invoke(args);

    internal TextEditor Editor => _editor;
}

public sealed class Caret(TextArea textArea)
{
    public int Offset
    {
        get => textArea.Editor.CaretOffset;
        set => textArea.Editor.CaretOffset = value;
    }

    public int Line => textArea.Document.GetLocation(Offset).Line;
    public int Column => textArea.Document.GetLocation(Offset).Column;
    public TextLocation Location => textArea.Document.GetLocation(Offset);
    public event EventHandler? PositionChanged;
    internal void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class Selection(TextArea textArea)
{
    public bool IsEmpty => textArea.Editor.SelectionLength == 0;
    public int Length => textArea.Editor.SelectionLength;
    public IReadOnlyList<ISegment> Segments
        => IsEmpty ? Array.Empty<ISegment>() : [new SimpleSegment(textArea.Editor.SelectionStart, Length)];
}

