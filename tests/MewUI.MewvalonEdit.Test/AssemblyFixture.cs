using Aprillz.MewUI;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
public static class AssemblyFixture
{
    /// <summary>
    /// Registers the graphics backend once before any test runs. Tests that read line metrics or
    /// lay the editor out measure text through it; swapping factories mid-run would race them.
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
