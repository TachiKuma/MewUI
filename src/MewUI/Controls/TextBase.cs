using System.Globalization;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for text input controls built on the managed text engine.
/// </summary>
// Rebuilt hierarchy (agent/textBase/plan.md): shared editing members migrate here from
// MultiLineTextBox slice by slice; LegacyTextBase remains frozen until TextBox and
// PasswordBox move onto this base.
public abstract class TextBase : Control, ITextCompositionClient, ITextCompositionEditor, ITextInputClient
{
    public static readonly MewProperty<ImeMode> ImeModeProperty =
        MewProperty<ImeMode>.Register<TextBase>(nameof(ImeMode), ImeMode.Auto);

    public static readonly MewProperty<string> PlaceholderProperty =
        MewProperty<string>.Register<TextBase>(nameof(Placeholder), string.Empty,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> IsReadOnlyProperty =
        MewProperty<bool>.Register<TextBase>(nameof(IsReadOnly), false,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> AcceptTabProperty =
        MewProperty<bool>.Register<TextBase>(nameof(AcceptTab), false);

    public static readonly MewProperty<int> MaxLengthProperty =
        MewProperty<int>.Register<TextBase>(nameof(MaxLength), 0);

    // Shared editing state: derived controls access the document/session directly, matching
    // the field names they used before the extraction.
    private protected readonly EditableTextDocument _document;
    private protected readonly TextEditorSession _editor;
    private protected bool _suppressNewLineInput;
    private protected bool _suppressTabInput;
    private protected int _compositionStart;
    private protected int _compositionLength;
    private protected CompositionAttr[]? _compositionAttributes;

    static TextBase()
    {
        FocusableProperty.OverrideDefaultValue<TextBase>(true);
    }

    protected TextBase()
        : this(new EditableTextDocument())
    {
    }

    protected TextBase(EditableTextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _editor = new TextEditorSession(_document);
        Cursor = CursorType.IBeam;
    }

    /// <summary>
    /// Gets or sets the IME mode for this text control.
    /// </summary>
    public ImeMode ImeMode
    {
        get => GetValue(ImeModeProperty);
        set => SetValue(ImeModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown while the document is empty.
    /// </summary>
    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets whether the text is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether Tab inserts a tab character instead of moving focus.
    /// </summary>
    public bool AcceptTab
    {
        get => GetValue(AcceptTabProperty);
        set => SetValue(AcceptTabProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum text length in UTF-16 code units. 0 means unlimited.
    /// </summary>
    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, Math.Max(0, value));
    }

    /// <summary>
    /// Gets or sets the caret index in document coordinates.
    /// </summary>
    public int CaretPosition
    {
        get => _editor.CaretPosition;
        set
        {
            _editor.SetCaret(value);
            EnsureCaretVisible();
        }
    }

    public event Action<TextInputEventArgs>? TextInput;
    public event Action<TextCompositionEventArgs>? TextCompositionStart;
    public event Action<TextCompositionEventArgs>? TextCompositionUpdate;
    public event Action<TextCompositionEventArgs>? TextCompositionEnd;

    /// <summary>Optional clipboard override for hosted editors and tests.</summary>
    public IClipboardService? ClipboardService { get; set; }

    /// <summary>
    /// Gets the currently selected text.
    /// </summary>
    public string SelectedText => _editor.Selection.Length == 0
        ? string.Empty
        : _document.GetText(_editor.Selection.Start, _editor.Selection.Length);

    public bool CanUndo => _editor.CanUndo;
    public bool CanRedo => _editor.CanRedo;

    public void Select(int start, int length) => _editor.SetSelection(start, length);

    public void SelectAll() => _editor.SelectAll();

    /// <summary>Scrolls the view so the caret is visible.</summary>
    public void ScrollToCaret() => EnsureCaretVisible();

    /// <summary>
    /// Appends text at the end of the document without allocating a full new Text string.
    /// </summary>
    public void AppendText(string? text, bool scrollToCaret = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        _editor.SetCaret(_document.TextLength);
        InsertText(text);
        if (scrollToCaret)
        {
            EnsureCaretVisible();
        }
    }

    public void ReplaceSelection(string? text)
    {
        if (IsReadOnly)
        {
            return;
        }
        InsertText(text);
        EnsureCaretVisible();
    }

    public void Undo()
    {
        if (!IsReadOnly)
        {
            _editor.Undo();
            EnsureCaretVisible();
        }
    }

    public void Redo()
    {
        if (!IsReadOnly)
        {
            _editor.Redo();
            EnsureCaretVisible();
        }
    }

    public void Copy()
    {
        if (_editor.Selection.Length > 0)
        {
            TrySetClipboardText(SelectedText);
        }
    }

    public void Cut()
    {
        if (IsReadOnly || _editor.Selection.Length == 0)
        {
            return;
        }
        Copy();
        _editor.ReplaceSelection(string.Empty);
    }

    public void Paste()
    {
        if (!IsReadOnly && TryGetClipboardText(out string text))
        {
            InsertText(text);
        }
    }

    private ContextMenu? _defaultContextMenu;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Right)
        {
            return;
        }

        // A user-assigned context menu is shown by the shared Control path instead.
        if (ContextMenu != null)
        {
            return;
        }

        ShowDefaultTextContextMenu(e.Position);
        e.Handled = true;
    }

    private protected void ShowDefaultTextContextMenu(Point positionInWindow)
    {
        var menu = _defaultContextMenu ??= new ContextMenu();
        bool hasSelection = _editor.Selection.Length > 0;
        bool canPaste = !IsReadOnly && TryGetClipboardText(out string clip) && !string.IsNullOrEmpty(clip);
        TextContextMenu.Show(menu, this, positionInWindow,
            undo: new TextMenuCommand(Undo, !IsReadOnly && CanUndo),
            redo: new TextMenuCommand(Redo, !IsReadOnly && CanRedo),
            cut: new TextMenuCommand(Cut, !IsReadOnly && hasSelection),
            copy: new TextMenuCommand(Copy, hasSelection),
            paste: new TextMenuCommand(Paste, canPaste),
            selectAll: new TextMenuCommand(SelectAll, _document.TextLength > 0));
    }

    private protected bool TrySetClipboardText(string text)
        => (ClipboardService ?? (Application.IsRunning ? Application.Current.PlatformHost.Clipboard : null))
            ?.TrySetText(text) == true;

    private protected bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        var clipboard = ClipboardService ?? (Application.IsRunning ? Application.Current.PlatformHost.Clipboard : null);
        return clipboard is not null && clipboard.TryGetText(out text);
    }

    /// <summary>
    /// Returns the rectangle at the given character index in window coordinates (DIPs).
    /// </summary>
    public abstract Rect GetCharRectInWindow(int charIndex);

    /// <summary>Scrolls the view so the caret is visible.</summary>
    private protected abstract void EnsureCaretVisible();

    bool ITextCompositionClient.IsComposing => _editor.IsComposing;
    int ITextCompositionClient.CompositionStartIndex => _compositionStart;

    int ITextCompositionEditor.CompositionLength => _compositionLength;
    (int Start, int End) ITextCompositionEditor.SelectionRange
        => (_editor.Selection.Start, _editor.Selection.Start + _editor.Selection.Length);
    void ITextCompositionEditor.SetSelectionRangeForPlatform(int start, int end)
        => _editor.SetSelection(Math.Min(start, end), Math.Abs(end - start));
    int ITextCompositionEditor.TextLength => _document.TextLength;
    string ITextCompositionEditor.GetTextSubstring(int start, int length) => _document.GetText(start, length);

    void ITextCompositionEditor.CommitActiveComposition()
    {
        if (!_editor.IsComposing) return;
        _editor.CommitComposition();
        _compositionLength = 0;
        _compositionAttributes = null;
        EnsureCaretVisible();
    }

    void ITextInputClient.HandleTextInput(TextInputEventArgs e)
    {
        TextInput?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        string text = e.Text ?? string.Empty;
        if (_suppressNewLineInput && (text.Contains('\r') || text.Contains('\n')))
        {
            _suppressNewLineInput = false;
            e.Handled = true;
            return;
        }
        if (_suppressTabInput && text.Contains('\t'))
        {
            _suppressTabInput = false;
            e.Handled = true;
            return;
        }
        if (_editor.IsComposing) _editor.CommitComposition();
        InsertText(text);
        EnsureCaretVisible();
        e.Handled = true;
    }

    void ITextCompositionClient.HandleTextCompositionStart(TextCompositionEventArgs e)
    {
        TextCompositionStart?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        _editor.BeginComposition();
        _compositionStart = _editor.CaretPosition;
        _compositionLength = 0;
    }

    void ITextCompositionClient.HandleTextCompositionUpdate(TextCompositionEventArgs e)
    {
        TextCompositionUpdate?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        if (!_editor.IsComposing)
        {
            _editor.BeginComposition();
            _compositionStart = _editor.CaretPosition;
        }
        UpdateCompositionText(e.Text);
        _compositionAttributes = e.Attributes;
        EnsureCaretVisible();
    }

    void ITextCompositionClient.HandleTextCompositionEnd(TextCompositionEventArgs e)
    {
        TextCompositionEnd?.Invoke(e);
        if (e.Handled || IsReadOnly) return;
        if (!string.IsNullOrEmpty(e.Text))
        {
            UpdateCompositionText(e.Text);
        }
        _editor.CommitComposition();
        _compositionLength = 0;
        _compositionAttributes = null;
        EnsureCaretVisible();
    }

    private protected void InsertText(string? value)
    {
        string text = EditableTextDocument.NormalizeNewLines(value ?? string.Empty);
        if (MaxLength > 0)
        {
            int remaining = MaxLength - (_document.TextLength - _editor.Selection.Length);
            if (remaining <= 0)
            {
                return;
            }
            if (text.Length > remaining)
            {
                text = TruncateAtTextElementBoundary(text, remaining);
            }
        }
        if (text.Length > 0)
        {
            _editor.ReplaceSelection(text);
        }
    }

    private void UpdateCompositionText(string? value)
    {
        string text = EditableTextDocument.NormalizeNewLines(value ?? string.Empty);
        if (MaxLength > 0)
        {
            int remaining = MaxLength - (_document.TextLength - _compositionLength);
            text = remaining <= 0
                ? string.Empty
                : TruncateAtTextElementBoundary(text, remaining);
        }
        _editor.UpdateComposition(text);
        _compositionLength = text.Length;
    }

    private protected static string TruncateAtTextElementBoundary(string text, int maximumLength)
    {
        if (maximumLength <= 0)
        {
            return string.Empty;
        }
        if (text.Length <= maximumLength)
        {
            return text;
        }

        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        int boundaryIndex = Array.BinarySearch(boundaries, maximumLength);
        int length = boundaryIndex >= 0
            ? maximumLength
            : boundaries[Math.Max(0, ~boundaryIndex - 1)];
        return length == 0 ? string.Empty : text[..length];
    }
}
