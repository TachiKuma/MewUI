using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Search;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The editor is a templated control around the surface that carries the document, so focusing the
/// editor has to reach that surface: typing goes nowhere otherwise.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EditorFocusTests
{
    [TestMethod]
    public void FocusingTheEditorReachesTheSurface()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (window, editor) = CreateHost();

        Assert.IsTrue(editor.Focus(), "the editor refused focus");

        Assert.IsTrue(editor.Surface.IsFocused, "focus stopped at the editor and never reached the surface");
    }

    [TestMethod]
    public void ClosingTheSearchPanelPutsTheKeyboardBackInTheDocument()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (window, editor) = CreateHost();
        var panel = SearchPanel.Install(editor);
        panel.Open();
        window.PerformLayout();

        panel.Close();
        window.PerformLayout();

        Assert.IsTrue(editor.Surface.IsFocused, "closing the panel left the keyboard nowhere");
    }

    private static (Window window, TextEditor editor) CreateHost()
    {
        var window = ScaledWindow.Create(1.0, 600, 300);
        var editor = new TextEditor { Text = "cat dog", SkipViewportCull = true, FontFamily = "Consolas", FontSize = 13 };
        window.Content = editor;
        window.PerformLayout();
        return (window, editor);
    }
}
