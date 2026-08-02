namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Tracks <see cref="TextLayout"/> instances with native handles.
/// Releases native resources when layouts are no longer referenced.
/// </summary>
public sealed class TextResourceTracker
{
    private sealed class Entry(WeakReference<TextLayout> weakRef, NativeHandleLease lease)
    {
        public readonly WeakReference<TextLayout> WeakRef = weakRef;
        public readonly NativeHandleLease Lease = lease;
    }

    private readonly LinkedList<Entry> _layouts = new();

    public void TrackLayout(TextLayout layout)
    {
        if (layout.BackendLease is { } lease && lease.Handle != 0)
        {
            _layouts.AddFirst(new Entry(new WeakReference<TextLayout>(layout), lease));
        }
    }

    public void Cleanup()
    {
        var node = _layouts.First;
        while (node != null)
        {
            var next = node.Next;
            if (!node.Value.WeakRef.TryGetTarget(out _))
            {
                node.Value.Lease.Release();
                _layouts.Remove(node);
            }
            node = next;
        }
    }

    public void ReleaseAll()
    {
        foreach (var entry in _layouts)
        {
            entry.Lease.Release();
        }
        _layouts.Clear();
    }
}
