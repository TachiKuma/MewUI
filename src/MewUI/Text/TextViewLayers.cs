namespace Aprillz.MewUI.Text;

/// <summary>
/// One entry of a text view's draw order. Layers paint only; input reaches elements through the
/// host, so a layer never takes part in hit testing.
/// </summary>
public interface ITextViewLayer
{
    void Draw(ITextRenderContext context, Rect viewportBounds);
}

/// <summary>Where a layer goes relative to the anchor it is inserted against.</summary>
public enum TextLayerPosition
{
    /// <summary>Under the anchor.</summary>
    Below,

    /// <summary>In place of the anchor, which stops drawing.</summary>
    Replace,

    /// <summary>Over the anchor.</summary>
    Above
}

/// <summary>
/// Draw order of a text view: the four built-in anchors plus whatever an extension inserted around
/// them. The host paints an anchor's own content when it is still present, so replacing one hands
/// that drawing to the caller.
/// </summary>
public sealed class TextViewLayerStack
{
    private readonly List<Entry> _entries;

    public TextViewLayerStack()
    {
        _entries = new List<Entry>(4);
        foreach (var anchor in Enum.GetValues<TextAdornmentLayer>())
        {
            _entries.Add(new Entry(anchor, null, IsAnchor: true));
        }
    }

    /// <summary>Raised when the order changed, so the host can repaint.</summary>
    public event Action? Changed;

    /// <summary>Layers in draw order. Built-in anchors appear as null-free entries only when replaced.</summary>
    public IReadOnlyList<ITextViewLayer> Layers
        => _entries.Where(entry => entry.Layer is not null).Select(entry => entry.Layer!).ToArray();

    /// <summary>Inserts <paramref name="layer"/> relative to <paramref name="anchor"/>.</summary>
    public void Insert(ITextViewLayer layer, TextAdornmentLayer anchor, TextLayerPosition position)
    {
        ArgumentNullException.ThrowIfNull(layer);
        int index = FindAnchor(anchor);
        switch (position)
        {
            case TextLayerPosition.Below:
                _entries.Insert(index, new Entry(anchor, layer, IsAnchor: false));
                break;
            case TextLayerPosition.Replace:
                _entries[index] = new Entry(anchor, layer, IsAnchor: true);
                break;
            default:
                _entries.Insert(index + 1, new Entry(anchor, layer, IsAnchor: false));
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Whether the host still owns the drawing of <paramref name="anchor"/>.</summary>
    public bool DrawsOwnContent(TextAdornmentLayer anchor) => _entries[FindAnchor(anchor)].Layer is null;

    /// <summary>
    /// Walks the order, letting the host paint an anchor it still owns through
    /// <paramref name="drawAnchor"/>.
    /// </summary>
    public void Draw(
        ITextRenderContext context,
        Rect viewportBounds,
        Action<TextAdornmentLayer> drawAnchor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(drawAnchor);
        foreach (var entry in _entries)
        {
            if (entry.Layer is null)
            {
                drawAnchor(entry.Anchor);
            }
            else
            {
                entry.Layer.Draw(context, viewportBounds);
            }
        }
    }

    private int FindAnchor(TextAdornmentLayer anchor)
    {
        for (int index = 0; index < _entries.Count; index++)
        {
            if (_entries[index].IsAnchor && _entries[index].Anchor == anchor)
            {
                return index;
            }
        }
        return _entries.Count - 1;
    }

    // A replaced anchor keeps IsAnchor so later inserts still find the position by name.
    private readonly record struct Entry(TextAdornmentLayer Anchor, ITextViewLayer? Layer, bool IsAnchor);
}
