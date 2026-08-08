using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.MewvalonEdit.Search;

/// <summary>
/// Routes the search keys to a panel: Ctrl+F opens it or puts the caret back in it, F3 and
/// Shift+F3 walk the matches, and Escape closes it.
/// </summary>
public sealed class SearchInputHandler : IDisposable
{
    private readonly UIElement _target;
    private readonly SearchPanel _panel;
    private bool _disposed;

    public SearchInputHandler(UIElement target, SearchPanel panel)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _target.KeyDown += OnKeyDown;
    }

    /// <summary>Handles one key. Returns whether it was a search key.</summary>
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
        if (key == Key.F3)
        {
            if (_panel.IsClosed)
            {
                return false;
            }
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

    private void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && Execute(e.Key, e.Modifiers))
        {
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _target.KeyDown -= OnKeyDown;
    }
}
