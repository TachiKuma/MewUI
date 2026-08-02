using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using System.Runtime.CompilerServices;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextResourceTrackerTests
{
    [TestMethod]
    public void CleanupAndFinalizerReleaseNativeHandleOnlyOnce()
    {
        int releaseCount = 0;
        var tracker = new TextResourceTracker();
        var weak = TrackLayout(tracker, () => Interlocked.Increment(ref releaseCount));

        GC.Collect();
        tracker.Cleanup();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        tracker.Cleanup();

        Assert.IsFalse(weak.TryGetTarget(out _));
        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    public void ReleaseAllAndFinalizerReleaseNativeHandleOnlyOnce()
    {
        int releaseCount = 0;
        var tracker = new TextResourceTracker();
        var layout = CreateLayout(() => Interlocked.Increment(ref releaseCount));
        tracker.TrackLayout(layout);

        tracker.ReleaseAll();
        Assert.AreEqual(1, releaseCount);
        Assert.AreEqual(0, layout.BackendHandle);

        layout = null!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.AreEqual(1, releaseCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TextLayout> TrackLayout(TextResourceTracker tracker, Action release)
    {
        var layout = CreateLayout(release);
        tracker.TrackLayout(layout);
        return new WeakReference<TextLayout>(layout);
    }

    private static TextLayout CreateLayout(Action release)
    {
        var layout = new TextLayout
        {
            MeasuredSize = Size.Empty,
            EffectiveBounds = Rect.Empty,
            EffectiveMaxWidth = 0,
            ContentHeight = 0
        };
        layout.AttachBackendHandle((nint)1, _ => release());
        return layout;
    }
}
