using Aprillz.MewUI;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class WindowlessRunTests
{
    private static Queue<IPlatformHost> Hosts => TestPlatformHosts.Queue;

    [TestMethod]
    public void Run_StartupHasDispatcherAndNoMainWindow()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        int calls = 0;

        Application.Run(() =>
        {
            calls++;
            Assert.AreSame(host.Dispatcher, Application.Current.Dispatcher);
            Assert.AreSame(host.SynchronizationContext, SynchronizationContext.Current);
            Assert.IsTrue(Application.Current.Dispatcher!.IsOnUIThread);
            Assert.IsEmpty(Application.Current.AllWindows);
            Application.Quit();
        });

        Assert.AreEqual(1, calls);
        Assert.IsNull(host.MainWindow);
        Assert.IsTrue(host.QuitCalled);
        Assert.IsTrue(host.Disposed);
    }

    [TestMethod]
    public void Run_WindowStartupRunsBeforeMainWindowIsShown()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        var host = new LifecyclePlatformHost(showMainWindow: true);
        Hosts.Enqueue(host);
        var window = HeadlessWindow.Create();
        bool loaded = false;
        bool startupRanBeforeLoaded = false;
        window.Loaded += () => loaded = true;

        Application.Run(window, () => startupRanBeforeLoaded = !loaded);

        Assert.IsTrue(startupRanBeforeLoaded);
        Assert.IsTrue(loaded);
        Assert.AreSame(window, host.MainWindow);
    }

    [TestMethod]
    public void Run_StartupFailureDoesNotPreventNextRun()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Application.Run(() => throw new InvalidOperationException("startup failure")));
        Assert.IsFalse(Application.IsRunning);

        var successful = new LifecyclePlatformHost();
        Hosts.Enqueue(successful);
        Application.Run(Application.Quit);

        Assert.IsFalse(Application.IsRunning);
        Assert.IsTrue(successful.QuitCalled);
    }

    [TestMethod]
    public void Run_StartupShownWindowIsRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The headless window test backend uses the Windows GDI graphics factory.");
            return;
        }

        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        var window = HeadlessWindow.Create();

        Application.Run(() =>
        {
            window.Show();
            CollectionAssert.Contains(Application.Current.AllWindows.ToArray(), window);
            Application.Quit();
        });
    }

    [TestMethod]
    public void Run_StartupRejectsNestedRun()
    {
        EnsureRegistered();
        Hosts.Enqueue(new LifecyclePlatformHost());
        InvalidOperationException? nested = null;

        Application.Run(() =>
        {
            nested = Assert.ThrowsExactly<InvalidOperationException>(
                () => Application.Run(Application.Quit));
            Application.Quit();
        });

        Assert.IsNotNull(nested);
        StringAssert.Contains(nested.Message, "already running");
    }

    [TestMethod]
    public void Run_RejectsNullStartup()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run((Window)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run((Action)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Application.Run(new Window(), null!));
    }

    [TestMethod]
    public void DefaultShutdownMode_WindowlessLastWindowCloseRequestsQuit()
    {
        EnsureRegistered();
        var previous = Application.ShutdownMode;
        Application.ShutdownMode = ShutdownMode.OnLastWindowClose;
        try
        {
            var host = new LifecyclePlatformHost();
            Hosts.Enqueue(host);
            Application.Run(() =>
            {
                var window = new Window();
                Application.Current.RegisterWindow(window);
                Application.Current.UnregisterWindow(window);
            });

            Assert.IsTrue(host.QuitCalled);
        }
        finally
        {
            Application.ShutdownMode = previous;
        }
    }

    [TestMethod]
    public void ExplicitShutdown_WindowlessLastWindowCloseKeepsRunningUntilQuit()
    {
        EnsureRegistered();
        var previous = Application.ShutdownMode;
        Application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var host = new LifecyclePlatformHost();
            Hosts.Enqueue(host);
            Application.Run(() =>
            {
                var window = new Window();
                Application.Current.RegisterWindow(window);
                Application.Current.UnregisterWindow(window);
                Assert.IsFalse(host.QuitCalled);
                Application.Quit();
            });

            Assert.IsTrue(host.QuitCalled);
        }
        finally
        {
            Application.ShutdownMode = previous;
        }
    }

    [TestMethod]
    public void MainWindowShutdown_WindowlessCloseKeepsRunningUntilQuit()
    {
        EnsureRegistered();
        var previous = Application.ShutdownMode;
        Application.ShutdownMode = ShutdownMode.OnMainWindowClose;
        try
        {
            var host = new LifecyclePlatformHost();
            Hosts.Enqueue(host);
            Application.Run(() =>
            {
                var window = new Window();
                Application.Current.RegisterWindow(window);
                Application.Current.UnregisterWindow(window);
                Assert.IsFalse(host.QuitCalled);
                Application.Quit();
            });

            Assert.IsTrue(host.QuitCalled);
        }
        finally
        {
            Application.ShutdownMode = previous;
        }
    }

    [TestMethod]
    public void Builder_OnStartupReplacesPreviousCallbackAndRunsWithoutFactory()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        int result = 0;

        Application.Create()
            .OnStartup(() => result = 1)
            .OnStartup(() =>
            {
                result = 2;
                Application.Quit();
            })
            .Run();

        Assert.AreEqual(2, result);
        Assert.IsNull(host.MainWindow);
    }

    [TestMethod]
    public void Builder_FactoryAndStartupUseWindowedRun()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var window = new Window();
        bool startupCalled = false;

        Application.Create()
            .BuildMainWindow(() => window)
            .OnStartup(() => startupCalled = true)
            .Run();

        Assert.IsTrue(startupCalled);
        Assert.AreSame(window, host.MainWindow);
    }

    [TestMethod]
    public void Builder_RunWindowAppliesStartup()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        var window = new Window();
        bool startupCalled = false;

        Application.Create()
            .OnStartup(() => startupCalled = true)
            .Run(window);

        Assert.IsTrue(startupCalled);
        Assert.AreSame(window, host.MainWindow);
    }

    [TestMethod]
    public void Builder_RunGenericAppliesStartup()
    {
        EnsureRegistered();
        var host = new LifecyclePlatformHost();
        Hosts.Enqueue(host);
        bool startupCalled = false;

        Application.Create()
            .OnStartup(() => startupCalled = true)
            .Run<Window>();

        Assert.IsTrue(startupCalled);
        Assert.IsInstanceOfType<Window>(host.MainWindow);
    }

    [TestMethod]
    public void Builder_RunWithoutFactoryOrStartupIsRejected()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => Application.Create().Run());

        StringAssert.Contains(error.Message, "BuildMainWindow");
        StringAssert.Contains(error.Message, "OnStartup");
    }

    private static void EnsureRegistered() => TestPlatformHosts.EnsureRegistered();

    private sealed class LifecyclePlatformHost(bool showMainWindow = false) : IPlatformHost
    {
        public bool Disposed { get; private set; }
        public bool QuitCalled { get; private set; }
        public Window? MainWindow { get; private set; }
        public ImmediateDispatcher Dispatcher { get; } = new();
        public SynchronizationContext SynchronizationContext { get; } = new();
        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => throw new NotSupportedException();
        public IDispatcher CreateDispatcher(nint windowHandle) => Dispatcher;
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

        public void Run(Application app, Window? mainWindow)
        {
            MainWindow = mainWindow;
            var previous = System.Threading.SynchronizationContext.Current;
            app.Dispatcher = Dispatcher;
            System.Threading.SynchronizationContext.SetSynchronizationContext(SynchronizationContext);
            try
            {
                app.OnHostLoopStarting(showMainWindow ? mainWindow : null);
            }
            finally
            {
                app.Dispatcher = null;
                System.Threading.SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        public void Quit(Application app) => QuitCalled = true;
        public void DoEvents() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsOnUIThread => true;
        public DispatcherOperation BeginInvoke(Action action) => throw new NotSupportedException();
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action) => throw new NotSupportedException();
        public void Invoke(Action action) => action();
    }
}
