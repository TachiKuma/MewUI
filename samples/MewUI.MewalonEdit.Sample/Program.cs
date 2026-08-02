using Aprillz.MewUI;
using MewvalonEdit.Sample;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("The initial MewalonEdit sample uses the Windows Direct2D backend.");

bool smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
Application
    .Create()
    .UseAccent(Accent.Purple)
    .UseWin32()
    .UseDirect2D()
    .BuildMainWindow(() =>
    {
        var window = new MainWindow();
        if (smoke) window.EnableSmokeTest();
        return window;
    })
    .Run();
