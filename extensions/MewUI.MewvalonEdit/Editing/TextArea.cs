using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

public sealed class TextArea
{
    private readonly TextEditor _editor;

    internal TextArea(TextEditor editor)
    {
        _editor = editor;
        Caret = new Caret(this);
        Selection = new TextSelection(this);
        TextView = new TextView(this);
        Attach(editor.Surface);
        editor.SurfaceChanged += OnSurfaceChanged;
    }

    public TextDocument Document => _editor.Document;
    public TextEditorOptions Options => _editor.Options;
    public Caret Caret { get; }
    public TextSelection Selection { get; }
    public TextView TextView { get; }

    public IIndentationStrategy? IndentationStrategy
    {
        get => _editor.IndentationStrategy;
        set => _editor.IndentationStrategy = value;
    }

    public event EventHandler? SelectionChanged;
    public event Action<TextInputEventArgs>? TextEntering;

    public void ReplaceSelection(string? text) => _editor.Surface.ReplaceSelection(text);

    private void Attach(MultiLineTextBox surface)
    {
        surface.EditingStateChanged += OnEditingStateChanged;
        surface.TextInput += OnTextInput;
    }

    private void Detach(MultiLineTextBox surface)
    {
        surface.EditingStateChanged -= OnEditingStateChanged;
        surface.TextInput -= OnTextInput;
    }

    private void OnSurfaceChanged(MultiLineTextBox previous, MultiLineTextBox current)
    {
        Detach(previous);
        Attach(current);
    }

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

public sealed class TextSelection(TextArea textArea)
{
    public bool IsEmpty => textArea.Editor.SelectionLength == 0;
    public int Length => textArea.Editor.SelectionLength;
    public IReadOnlyList<ISegment> Segments
        => IsEmpty ? Array.Empty<ISegment>() : [new SimpleSegment(textArea.Editor.SelectionStart, Length)];
}

public sealed class TextView(TextArea textArea)
{
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

    public void Redraw() => textArea.Editor.InvalidateTextView();
}
