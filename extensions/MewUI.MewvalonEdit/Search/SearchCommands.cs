using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Search;

/// <summary>
/// The input handler that registers the search keys: Ctrl+F opens the panel or puts the caret back
/// in it, F3 and Shift+F3 walk the matches, and Escape closes it. Nested in the text area's default
/// handler, as the original nests it, so the surface path sees the keys before the editor acts.
/// </summary>
public sealed class SearchInputHandler : TextAreaInputHandler
{
    public SearchInputHandler(TextArea textArea, SearchPanel panel) : base(textArea)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Panel = panel;
        AddBinding(new KeyBinding(
            new KeyGesture(Key.F, ModifierKeys.Primary), () => Panel.Open()));
        AddBinding(new KeyBinding(
            new KeyGesture(Key.F3), () => Panel.FindNext(), () => !Panel.IsClosed));
        AddBinding(new KeyBinding(
            new KeyGesture(Key.F3, ModifierKeys.Shift), () => Panel.FindPrevious(), () => !Panel.IsClosed));
        AddBinding(new KeyBinding(
            new KeyGesture(Key.Escape), Panel.Close, () => !Panel.IsClosed));
    }

    /// <summary>The panel the keys drive.</summary>
    public SearchPanel Panel { get; }

    /// <summary>Offers one key to the search bindings. Returns whether one of them took it.</summary>
    public bool Execute(Key key, ModifierKeys modifiers)
        => TryHandleKey(new KeyEventArgs(key, platformKey: 0, modifiers));

    /// <summary>
    /// The nested path only sees keys while the editing surface has the focus. Keys bubble from the
    /// focused control to the root, so listening on the editor as well is what lets Ctrl+F work
    /// from the search box or a margin - the area AvalonEdit's routed commands cover.
    /// </summary>
    public override void Attach()
    {
        base.Attach();
        TextArea.Editor.KeyDown += OnEditorKeyDown;
    }

    public override void Detach()
    {
        TextArea.Editor.KeyDown -= OnEditorKeyDown;
        base.Detach();
    }

    private void OnEditorKeyDown(KeyEventArgs e)
    {
        if (!e.Handled)
        {
            TryHandleKey(e);
        }
    }
}
