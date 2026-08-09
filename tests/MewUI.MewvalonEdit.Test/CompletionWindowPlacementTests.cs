using System.ComponentModel;
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
    public void InsightWindowAlsoOpensPastTheEditorBottom()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow(string.Join('\n', Enumerable.Repeat("Method(", 40)));
        editor.CaretOffset = editor.Document.TextLength;
        editor.TextArea.Caret.BringCaretToView();
        window.PerformLayout();

        var insight = new OverloadInsightWindow(editor.TextArea)
        {
            Provider = new SingleOverloadProvider()
        };
        insight.Show();

        Assert.IsGreaterThan(0, insight.PlacedBounds.Height, "the popup reported no placement");
        Assert.IsGreaterThan(editor.Bounds.Bottom, insight.PlacedBounds.Bottom,
            $"the insight window stopped at the editor bottom (editor={editor.Bounds}, popup={insight.PlacedBounds})");
        insight.Close();
    }

    [TestMethod]
    public void TheWindowIsAsTallAsItsRowsAndShrinksWhenFilteringDoes()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = OpenWindow(editor);

        double rowHeight = completion.PlacedBounds.Height / WORDS.Length;
        Assert.IsGreaterThan(10, rowHeight, $"the window collapsed to a fraction of a row: {completion.PlacedBounds}");

        editor.TextArea.PerformTextInput("De");
        int visible = completion.CompletionList.VisibleItems.Count;

        Assert.IsLessThan(WORDS.Length, visible, "the query did not narrow the list");
        Assert.AreEqual(visible * rowHeight, completion.PlacedBounds.Height, 1.0,
            "the window kept the height of the unfiltered list");
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

    private sealed class SingleOverloadProvider : IOverloadProvider
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int SelectedIndex { get; set; }
        public int Count => 1;
        public string CurrentIndexText => "1 of 1";
        public object? CurrentHeader => "Method(int value)";
        public object? CurrentContent => "the only overload";
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
