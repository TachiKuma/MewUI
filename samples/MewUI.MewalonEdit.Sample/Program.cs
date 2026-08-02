using Aprillz.MewUI;
using ICSharpCode.AvalonEdit.MewUI.Sample;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("The initial MewalonEdit sample uses the Windows Direct2D backend.");

Win32Platform.Register();
Direct2DBackend.Register();

var window = new MainWindow();
if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
    window.EnableSmokeTest();

Application.Run(window);
