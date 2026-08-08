using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Search;

/// <summary>
/// The input handler that registers the search keys: Ctrl+F opens the panel or puts the caret back
/// in it, F3 and Shift+F3 walk the matches, and Escape closes it. Nested in the text area's default
/// handler as the original nests it; attaching puts the bindings on the editor element, which is
/// the subtree AvalonEdit's routed commands cover - they answer wherever the focus is inside it.
/// </summary>
public sealed class SearchInputHandler : TextAreaInputHandler
{
    private readonly KeyBinding[] _searchBindings;

    public SearchInputHandler(TextArea textArea, SearchPanel panel) : base(textArea)
    {
        ArgumentNullException.ThrowIfNull(panel);
        Panel = panel;
        _searchBindings =
        [
            new KeyBinding(new KeyGesture(Key.F, ModifierKeys.Primary), () => Panel.Open()),
            new KeyBinding(new KeyGesture(Key.F3), () => Panel.FindNext(), () => !Panel.IsClosed),
            new KeyBinding(
                new KeyGesture(Key.F3, ModifierKeys.Shift), () => Panel.FindPrevious(), () => !Panel.IsClosed),
            new KeyBinding(new KeyGesture(Key.Escape), Panel.Close, () => !Panel.IsClosed)
        ];
    }

    /// <summary>The panel the keys drive.</summary>
    public SearchPanel Panel { get; }

    /// <summary>Offers one key to the search bindings. Returns whether one of them took it.</summary>
    public bool Execute(Key key, ModifierKeys modifiers)
    {
        var args = new KeyEventArgs(key, platformKey: 0, modifiers);
        foreach (var binding in _searchBindings)
        {
            if (binding.TryHandle(args))
            {
                return true;
            }
        }
        return false;
    }

    public override void Attach()
    {
        base.Attach();
        foreach (var binding in _searchBindings)
        {
            TextArea.Editor.KeyBindings.Add(binding);
        }
    }

    public override void Detach()
    {
        foreach (var binding in _searchBindings)
        {
            TextArea.Editor.KeyBindings.Remove(binding);
        }
        base.Detach();
    }
}
