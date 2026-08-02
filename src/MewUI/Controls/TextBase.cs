using System.Globalization;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
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

    private static readonly MewPropertyKey<int> SelectionStartPropertyKey =
        MewProperty<int>.RegisterReadOnly<TextBase>(nameof(SelectionStart), 0);

    public static readonly MewProperty<int> SelectionStartProperty = SelectionStartPropertyKey.Property;

    private static readonly MewPropertyKey<int> SelectionLengthPropertyKey =
        MewProperty<int>.RegisterReadOnly<TextBase>(nameof(SelectionLength), 0);

    public static readonly MewProperty<int> SelectionLengthProperty = SelectionLengthPropertyKey.Property;

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
    private protected bool _caretVisible = true;
    private protected bool _syncingText;
    private string _textSnapshot = string.Empty;
    private long _textSnapshotVersion = -1;
    private DispatcherTimer? _caretTimer;

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
        _document.Changed += OnDocumentTextChanged;
        _editor.StateChanged += SyncSelectionMirrors;

        if (_document.TextLength > 0 && TextSyncProperty is MewProperty<string> mirror)
        {
            _syncingText = true;
            try
            {
                SetValue(mirror, GetTextSnapshot());
            }
            finally
            {
                _syncingText = false;
            }
        }
    }

    /// <summary>
    /// The control-declared property mirroring the document text (Text, Password, ...).
    /// The base never exposes a Text property itself; document changes are committed to this
    /// mirror so controls decide the name and shape of their text surface.
    /// </summary>
    private protected virtual MewProperty<string>? TextSyncProperty => null;

    public int SelectionStart => GetValue(SelectionStartProperty);
    public int SelectionLength => GetValue(SelectionLengthProperty);

    public event Action<string>? TextChanged;

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
    /// Returns the raw selected document text. SelectedText is deliberately not exposed on the
    /// base: only controls whose text is public (TextBox, MultiLineTextBox) surface it.
    /// </summary>
    private protected string GetSelectedDocumentText() => _editor.Selection.Length == 0
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
            CopyToClipboardCore();
        }
    }

    public void Cut()
    {
        if (IsReadOnly || _editor.Selection.Length == 0)
        {
            return;
        }
        CutToClipboardCore();
    }

    public void Paste()
    {
        if (!IsReadOnly && TryGetClipboardText(out string text))
        {
            PasteFromClipboardCore(text);
        }
    }

    /// <summary>Writes the selection to the clipboard. Masking controls override to withhold it.</summary>
    private protected virtual void CopyToClipboardCore() => TrySetClipboardText(GetSelectedDocumentText());

    /// <summary>Cuts the selection. Masking controls override to withhold the clipboard write.</summary>
    private protected virtual void CutToClipboardCore()
    {
        CopyToClipboardCore();
        _editor.ReplaceSelection(string.Empty);
    }

    /// <summary>Inserts clipboard text at the selection after per-control normalization.</summary>
    private protected virtual void PasteFromClipboardCore(string text)
    {
        InsertText(NormalizePastedText(text));
        EnsureCaretVisible();
    }

    /// <summary>Per-control paste normalization (single-line controls convert newlines to spaces).</summary>
    private protected virtual string NormalizePastedText(string text) => text;

    /// <summary>
    /// Handles the shared primary-modifier editing shortcuts. Returns whether the key was consumed.
    /// </summary>
    private protected bool HandlePrimaryKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.A:
                SelectAll();
                return true;
            case Key.C:
                Copy();
                return true;
            case Key.X:
                Cut();
                return true;
            case Key.V:
                Paste();
                return true;
            case Key.Z:
                if (e.ShiftKey) Redo(); else Undo();
                return true;
            case Key.Y:
                Redo();
                return true;
            case Key.Home:
                _editor.SetCaret(0, e.ShiftKey);
                return true;
            case Key.End:
                _editor.SetCaret(_document.TextLength, e.ShiftKey);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Draws clause-segmented IME composition underlines using caret geometry from the view.
    /// Wrapped segments underline to <paramref name="wrapRightEdge"/> on their starting line.
    /// </summary>
    private protected void DrawCompositionUnderlines(IGraphicsContext context, double wrapRightEdge)
    {
        if (!_editor.IsComposing || _compositionLength <= 0)
        {
            return;
        }

        var color = Theme.Palette.WindowText;
        int index = 0;
        while (index < _compositionLength)
        {
            var attr = GetCompositionAttr(index);
            var startRect = GetCharRectInWindow(_compositionStart + index);
            double lineY = startRect.Y;

            int segmentEnd = index + 1;
            var endRect = GetCharRectInWindow(_compositionStart + segmentEnd);
            while (segmentEnd < _compositionLength && GetCompositionAttr(segmentEnd) == attr && endRect.Y == lineY)
            {
                segmentEnd++;
                endRect = GetCharRectInWindow(_compositionStart + segmentEnd);
            }

            double endX = endRect.Y == lineY ? endRect.X : wrapRightEdge;
            DrawCompositionUnderline(context, startRect.X, endX, lineY + startRect.Height, color, attr);
            index = segmentEnd;
        }
    }

    private CompositionAttr GetCompositionAttr(int offsetInComposition)
        => _compositionAttributes is { Length: > 0 } attrs && offsetInComposition < attrs.Length
            ? attrs[offsetInComposition]
            : CompositionAttr.Input;

    private static void DrawCompositionUnderline(
        IGraphicsContext context, double startX, double endX, double y, Color color, CompositionAttr attr)
    {
        double thickness = attr is CompositionAttr.TargetConverted or CompositionAttr.TargetNotConverted ? 2 : 1;
        bool dashed = attr is CompositionAttr.Input or CompositionAttr.TargetNotConverted;

        if (!dashed)
        {
            context.DrawLine(new Point(startX, y), new Point(endX, y), color, thickness, pixelSnap: true);
            return;
        }

        const double DASH = 3;
        const double GAP = 2;
        double x = startX;
        while (x < endX)
        {
            double dashEnd = Math.Min(x + DASH, endX);
            context.DrawLine(new Point(x, y), new Point(dashEnd, y), color, thickness, pixelSnap: true);
            x = dashEnd + GAP;
        }
    }

    /// <summary>Per-control typed-text normalization (single-line controls drop newline characters).</summary>
    private protected virtual string NormalizeTypedText(string text) => text;

    /// <summary>Per-control normalization of externally assigned mirror-property text.</summary>
    private protected virtual string NormalizeExternalText(string text) => text;

    /// <summary>
    /// Applies an externally assigned mirror-property value to the document. Control text
    /// property callbacks route here.
    /// </summary>
    private protected void ApplyExternalTextCore(string value)
    {
        if (_syncingText)
        {
            return;
        }
        _syncingText = true;
        try
        {
            _editor.CommitComposition();
            string normalized = NormalizeExternalText(EditableTextDocument.NormalizeNewLines(value ?? string.Empty));
            _document.SetText(normalized);
            _textSnapshot = normalized;
            _textSnapshotVersion = _document.Version;
            _editor.ClearHistory();
            _editor.SetCaret(Math.Min(_editor.CaretPosition, _document.TextLength));
        }
        finally
        {
            _syncingText = false;
        }
    }

    private protected string GetTextSnapshot()
    {
        if (_textSnapshotVersion != _document.Version)
        {
            _textSnapshot = _document.ToString();
            _textSnapshotVersion = _document.Version;
        }
        return _textSnapshot;
    }

    private void OnDocumentTextChanged(TextChange change)
    {
        _textSnapshotVersion = -1;
        string? currentText = null;
        var mirror = TextSyncProperty;
        if (!_syncingText && mirror is not null && (HasPropertyBinding(mirror.Id) || HasTextChangedSubscribers))
        {
            _syncingText = true;
            try
            {
                currentText = _document.ToString();
                CommitTargetValue(mirror, currentText);
            }
            finally
            {
                _syncingText = false;
            }
        }
        if (HasTextChangedSubscribers)
        {
            currentText ??= _document.ToString();
            RaiseTextChanged(currentText);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Whether raising the text-changed notification is worth materializing the full text.</summary>
    private protected virtual bool HasTextChangedSubscribers => TextChanged is not null;

    /// <summary>Raises the text-changed notification. Masking controls redirect it (e.g. PasswordChanged).</summary>
    private protected virtual void RaiseTextChanged(string text) => TextChanged?.Invoke(text);

    private void SyncSelectionMirrors()
    {
        var selection = _editor.Selection;
        SetValue(SelectionStartPropertyKey, selection.Start);
        SetValue(SelectionLengthPropertyKey, selection.Length);
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        StartCaretBlink();
        if (ImeMode != ImeMode.Auto && FindVisualRoot() is Window { Backend: not null } window)
        {
            window.Backend.SetImeMode(ImeMode);
        }
    }

    protected override void OnLostFocus()
    {
        StopCaretBlink();
        _caretVisible = true;
        if (_editor.IsComposing) _editor.CommitComposition();
        if (ImeMode != ImeMode.Auto && FindVisualRoot() is Window { Backend: not null } window)
        {
            window.Backend.SetImeMode(ImeMode.Auto);
        }
        base.OnLostFocus();
    }

    private protected void StartCaretBlink()
    {
        StopCaretBlink();
        _caretVisible = true;
        _caretTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500));
        _caretTimer.Tick += OnCaretBlink;
        _caretTimer.Start();
    }

    private protected void StopCaretBlink()
    {
        if (_caretTimer is null) return;
        _caretTimer.Stop();
        _caretTimer.Tick -= OnCaretBlink;
    }

    private protected void ResetCaretBlink()
    {
        if (IsFocused) StartCaretBlink();
        else _caretVisible = true;
    }

    private void OnCaretBlink()
    {
        _caretVisible = !_caretVisible;
        InvalidateVisual();
    }

    protected override void OnDispose()
    {
        StopCaretBlink();
        _document.Changed -= OnDocumentTextChanged;
        _editor.StateChanged -= SyncSelectionMirrors;
        base.OnDispose();
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

    private protected virtual void ShowDefaultTextContextMenu(Point positionInWindow)
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
        text = NormalizeTypedText(text);
        if (text.Length == 0)
        {
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
