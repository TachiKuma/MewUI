using Aprillz.MewUI;

namespace MewUI.MewvalonEdit.Test.Infrastructure;

[TestClass]
public static class AssemblyFixture
{
    /// <summary>
    /// Registers the process-wide graphics factory once before any test runs, mirroring the core
    /// suite; layout-driven tests (input routing, visual lines) measure text through it.
    /// </summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            GdiBackend.Register();
        }
    }
}
