using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// Element-level coverage of link clicking: the generator builds the element through the factory
/// seam and the element's own handlers decide navigation. The window-to-element half of the chain
/// runs on core internals and is verified in the sample, not here.
/// </summary>
[TestClass]
public sealed class LinkClickRoutingTests
{
    private const string TEXT = "see https://example.com/docs now";

    private sealed class RecordingLinkText(string text, int documentLength, TextRunStyle style)
        : VisualLineLinkText(text, documentLength, style)
    {
        public List<string> Navigated { get; } = [];

        protected override void NavigateTo(string uri) => Navigated.Add(uri);
    }

    private sealed class RecordingLinkGenerator : LinkElementGenerator
    {
        protected override VisualLineLinkText CreateLinkElement(string text, int documentLength, TextRunStyle style)
            => new RecordingLinkText(text, documentLength, style);
    }

    private static RecordingLinkText ConstructLink()
    {
        var editor = new TextEditor { Text = TEXT };
        var generator = new RecordingLinkGenerator();
        editor.TextArea.TextView.ElementGenerators.Add(generator);

        generator.StartGeneration(new ConstructionContext(editor));
        var element = generator.ConstructElement(TEXT.IndexOf("https", StringComparison.Ordinal));
        generator.FinishGeneration();
        return (RecordingLinkText)element!;
    }

    private static MouseEventArgs LeftClick(ModifierKeys modifiers)
        => new(default, default, MouseButton.Left, leftButton: true, modifiers: modifiers);

    [TestMethod]
    public void ControlClickNavigatesAndClaimsTheEvent()
    {
        var link = ConstructLink();

        var click = LeftClick(ModifierKeys.Control);
        link.OnMouseDown(click);

        Assert.ContainsSingle(link.Navigated);
        Assert.AreEqual("https://example.com/docs", link.Navigated[0]);
        Assert.IsTrue(click.Handled, "A followed link must claim the press so the caret stays put.");
    }

    [TestMethod]
    public void PlainClickLeavesTheEventForCaretPlacement()
    {
        var link = ConstructLink();

        var click = LeftClick(ModifierKeys.None);
        link.OnMouseDown(click);

        Assert.IsEmpty(link.Navigated);
        Assert.IsFalse(click.Handled, "An unclaimed press falls through to the editor's caret.");
    }

    [TestMethod]
    public void WithoutTheControlRequirementAPlainClickNavigates()
    {
        var link = ConstructLink();
        link.RequireControlModifierForClick = false;

        var click = LeftClick(ModifierKeys.None);
        link.OnMouseDown(click);

        Assert.ContainsSingle(link.Navigated);
        Assert.IsTrue(click.Handled);
    }

    private sealed class ConstructionContext(TextEditor editor) : ITextRunConstructionContext
    {
        public Aprillz.MewUI.MewvalonEdit.Document.TextDocument Document => editor.Document;
        public Aprillz.MewUI.MewvalonEdit.Document.DocumentLine CurrentDocumentLine
            => editor.Document.GetLineByNumber(1);
        public TextRunStyle DefaultStyle => new(editor.FontFamily, editor.FontSize, editor.FontWeight);
    }
}
