using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace MewUI.MewvalonEdit.Test.Infrastructure;

/// <summary>
/// No-op window backend with a fake native handle, mirroring the core test suite's, so layout and
/// input routing run headless.
/// </summary>
internal sealed class HeadlessWindowBackend : IWindowBackend
{
    public nint Handle => 1;
    public CursorType LastCursor { get; private set; } = CursorType.Arrow;
    public void SetResizable(bool resizable) { }
    public void PresentSurface() { }
    public void Hide() { }
    public void Close() { }
    public void Invalidate(bool erase) { }
    public void SetTitle(string title) { }
    public void SetIcon(IconSource? icon) { }
    public void SetClientSize(double widthDip, double heightDip) { }
    public Point GetPosition() => default;
    public void SetPosition(double leftDip, double topDip) { }
    public void SetPositionPx(int leftPx, int topPx) { }
    public void CaptureMouse() { }
    public void ReleaseMouseCapture() { }
    public Point ClientToScreen(Point clientPointDip) => clientPointDip;
    public Point ScreenToClient(Point screenPointPx) => screenPointPx;
    public void CenterOnOwner() { }
    public void EnsureTheme(bool isDark) { }
    public void Activate() { }
    public void SetOwner(nint ownerHandle) { }
    public void SetEnabled(bool enabled) { }
    public void SetOpacity(double opacity) { }
    public void SetAllowsTransparency(bool allowsTransparency) { }
    public void SetCursor(CursorType cursorType) => LastCursor = cursorType;
    public void SetImeMode(ImeMode mode) { }
    public void CancelImeComposition() { }
    public void Dispose() { }
}

internal static class HeadlessEditorHost
{
    public static Window CreateWindow(double width = 600, double height = 300)
    {
        var window = new Window();
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(width, height);
        return window;
    }

    public static void SendClick(this Window window, Point position, ModifierKeys modifiers = ModifierKeys.None)
    {
        WindowInputRouter.MouseMove(window, position, position,
            leftDown: false, rightDown: false, middleDown: false);
        WindowInputRouter.MouseButton(window, position, position, MouseButton.Left, isDown: true,
            leftDown: true, rightDown: false, middleDown: false, clickCount: 1, modifiers: modifiers);
        WindowInputRouter.MouseButton(window, position, position, MouseButton.Left, isDown: false,
            leftDown: false, rightDown: false, middleDown: false, clickCount: 1, modifiers: modifiers);
    }

    public static void SendMouseMove(this Window window, Point position, ModifierKeys modifiers = ModifierKeys.None)
        => WindowInputRouter.MouseMove(window, position, position,
            leftDown: false, rightDown: false, middleDown: false, modifiers: modifiers);
}
