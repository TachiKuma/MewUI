using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.CodeCompletion;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The completion window rides a popup owned by the editor, so its placement is bounded by the
/// window rather than by the editor: a caret on the last visible line still opens a full list
/// downward. The assertions read the placement decision, which is what the popup surface then
/// positions itself from.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CompletionWindowPlacementTests
{
    private const double EDITOR_HEIGHT = 150;

    private static readonly string[] WORDS =
        ["DateTime", "DateTimeKind", "Debug", "Decimal", "Delegate", "Dictionary", "Directory"];

    [TestMethod]
    public void CaretOnTheLastLineOpensPastTheEditorBottom()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow(string.Join('\n', Enumerable.Repeat("line", 40)));
        editor.CaretOffset = editor.Document.TextLength;
        editor.TextArea.Caret.BringCaretToView();
        window.PerformLayout();

        var completion = OpenWindow(editor);

        Assert.IsTrue(completion.IsOpen);
        Assert.IsGreaterThan(0, completion.PlacedBounds.Height, "the popup reported no placement");
        Assert.IsGreaterThan(editor.Bounds.Bottom, completion.PlacedBounds.Bottom,
            $"the list stopped at the editor bottom (editor={editor.Bounds}, popup={completion.PlacedBounds})");
        completion.Close();
    }

    [TestMethod]
    public void TypingKeepsTheWindowOpen()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = OpenWindow(editor);

        editor.TextArea.PerformTextInput("De");

        Assert.IsTrue(completion.IsOpen, "typing closed the completion window");
        completion.Close();
    }

    [TestMethod]
    public void ScrollingMovesTheWindowWithTheAnchor()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow(string.Join('\n', Enumerable.Repeat("line", 200)));
        editor.CaretOffset = editor.Document.GetLineByNumber(20).Offset;
        editor.TextArea.Caret.BringCaretToView();
        window.PerformLayout();

        var completion = OpenWindow(editor);
        double before = completion.PlacedBounds.Y;

        // One line, so the anchor stays in the viewport: leaving it is what closes the window.
        editor.LineDown();
        window.PerformLayout();

        Assert.IsTrue(completion.IsOpen, "scrolling closed the completion window");
        Assert.AreNotEqual(before, completion.PlacedBounds.Y, "the window did not follow its anchor");
        completion.Close();
    }

    private static (TextEditor editor, Window window) CreateEditorInWindow(string text)
    {
        var window = ScaledWindow.Create(1.0);
        var editor = new TextEditor
        {
            Text = text,
            SkipViewportCull = true,
            FontFamily = "Consolas",
            FontSize = 13,
            Height = EDITOR_HEIGHT,
            VerticalAlignment = VerticalAlignment.Top
        };
        window.Content = editor;
        window.PerformLayout();
        return (editor, window);
    }

    private static CompletionWindow OpenWindow(TextEditor editor)
    {
        var completion = new CompletionWindow(editor.TextArea);
        foreach (string word in WORDS)
        {
            completion.CompletionList.CompletionData.Add(new CompletionData(word));
        }
        completion.Show();
        return completion;
    }
}
