namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Text layout measurement result produced by <see cref="IGraphicsContext.CreateTextLayout"/>.
/// Pure managed result. Backend may attach a native handle internally for rendering.
/// </summary>
public sealed class TextLayout
{
    private Action<nint>? _releaseBackendHandle;
    private nint _backendHandle;

    public required Size MeasuredSize { get; init; }

    public required Rect EffectiveBounds { get; set; }

    public required double EffectiveMaxWidth { get; init; }

    public required double ContentHeight { get; init; }

    /// <summary>Backend-private native handle for rendering.</summary>
    internal nint BackendHandle => _backendHandle;

    internal void AttachBackendHandle(nint handle, Action<nint> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        ReleaseBackendHandle();
        _backendHandle = handle;
        _releaseBackendHandle = release;
    }

    internal void ReleaseBackendHandle()
    {
        nint handle = Interlocked.Exchange(ref _backendHandle, 0);
        var release = Interlocked.Exchange(ref _releaseBackendHandle, null);
        if (handle != 0)
        {
            release?.Invoke(handle);
        }
    }

    ~TextLayout() => ReleaseBackendHandle();
}
