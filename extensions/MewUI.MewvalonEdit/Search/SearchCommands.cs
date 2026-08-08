using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Search;

/// <summary>
/// Routes the search keys to a panel: Ctrl+F opens it or puts the caret back in it, F3 and
/// Shift+F3 walk the matches, and Escape closes it. Stacked on the text area so it sees the keys
/// before the editor acts on them.
/// </summary>
public sealed class SearchInputHandler(TextArea textArea, SearchPanel panel)
    : TextAreaStackedInputHandler(textArea)
{
    private readonly SearchPanel _panel = panel ?? throw new ArgumentNullException(nameof(panel));

    /// <summary>Acts on one key. Returns whether it was a search key.</summary>
    public bool Execute(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F && (modifiers & ModifierKeys.Control) != 0)
        {
            _panel.Open();
            return true;
        }
        if (key == Key.Escape && !_panel.IsClosed)
        {
            _panel.Close();
            return true;
        }
        if (key == Key.F3 && !_panel.IsClosed)
        {
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                _panel.FindPrevious();
            }
            else
            {
                _panel.FindNext();
            }
            return true;
        }
        return false;
    }

    public override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!e.Handled && Execute(e.Key, e.Modifiers))
        {
            e.Handled = true;
        }
    }
}
