using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// Drives the whole input chain headless: window routing, the surface's public mouse events, the
/// editor's coordinate-to-element lookup, and the link element's own handlers.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LinkClickRoutingTests
{
    private const string TEXT = "see https://example.com/docs now";

    private sealed class RecordingLinkText(string text, int documentLength, TextRunStyle style)
        : VisualLineLinkText(text, documentLength, style)
    {
        public static List<string> Navigated { get; } = [];

        protected override void NavigateTo(string uri) => Navigated.Add(uri);
    }

    private sealed class RecordingLinkGenerator : LinkElementGenerator
    {
        protected override VisualLineLinkText CreateLinkElement(string text, int documentLength, TextRunStyle style)
            => new RecordingLinkText(text, documentLength, style);
    }

    private static (TextEditor Editor, Window Window, Point LinkPoint) CreateEditorWithLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        RecordingLinkText.Navigated.Clear();
        var editor = new TextEditor { Text = TEXT, ShowLineNumbers = false };
        editor.TextArea.TextView.ElementGenerators.Add(new RecordingLinkGenerator());
        var window = HeadlessEditorHost.CreateWindow();
        window.Content = editor;
        window.PerformLayout();

        // The link is one inline cluster, so caret slots inside it collapse to its edges. The
        // midpoint between the slots at its start and end is safely inside the drawn range.
        var surface = editor.TextArea.TextView.Surface;
        var startRect = surface.GetCharRectInWindow(TEXT.IndexOf("https", StringComparison.Ordinal));
        var endRect = surface.GetCharRectInWindow(TEXT.IndexOf(" now", StringComparison.Ordinal));
        return (editor, window, new Point(
            (startRect.X + endRect.X) / 2,
            startRect.Y + startRect.Height / 2));
    }

    [TestMethod]
    public void ControlClickOnALinkNavigatesAndLeavesTheCaretAlone()
    {
        (var editor, var window, var linkPoint) = CreateEditorWithLink();
        editor.CaretOffset = 0;

        window.SendClick(linkPoint, ModifierKeys.Control);

        Assert.ContainsSingle(RecordingLinkText.Navigated);
        Assert.AreEqual("https://example.com/docs", RecordingLinkText.Navigated[0]);
        Assert.AreEqual(0, editor.CaretOffset, "A handled link click must not move the caret.");
    }

    [TestMethod]
    public void PlainClickOnALinkPlacesTheCaretWithoutNavigating()
    {
        (var editor, var window, var linkPoint) = CreateEditorWithLink();
        editor.CaretOffset = 0;

        window.SendClick(linkPoint);

        Assert.IsEmpty(RecordingLinkText.Navigated);
        Assert.IsGreaterThan(0, editor.CaretOffset, "A plain click still places the caret.");
    }

    [TestMethod]
    public void HoveringALinkWithControlShowsTheHandCursor()
    {
        (var editor, var window, var linkPoint) = CreateEditorWithLink();

        window.SendMouseMove(linkPoint, ModifierKeys.Control);
        Assert.AreEqual(CursorType.Hand, editor.TextArea.TextView.Surface.Cursor);

        window.SendMouseMove(linkPoint);
        Assert.AreEqual(CursorType.IBeam, editor.TextArea.TextView.Surface.Cursor);
    }
}
