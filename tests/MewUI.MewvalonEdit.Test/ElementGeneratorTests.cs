using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class ElementGeneratorTests
{
    /// <summary>Replaces every "@" with a marker, exercising the scan protocol directly.</summary>
    private sealed class MarkerGenerator(string replacement) : VisualLineElementGenerator
    {
        public List<int> AskedFrom { get; } = [];

        public override int GetFirstInterestedOffset(int startOffset)
        {
            AskedFrom.Add(startOffset);
            var line = CurrentContext!.CurrentDocumentLine;
            string text = CurrentContext.Document.GetText(line.Offset, line.Length);
            int index = text.IndexOf('@', Math.Max(0, startOffset - line.Offset));
            return index < 0 ? -1 : line.Offset + index;
        }

        public override VisualLineElement? ConstructElement(int offset)
            => new TextReplacementElement(replacement, 1, CurrentContext!.DefaultStyle);
    }

    private static List<InlineRun> Generate(TextEditor editor)
    {
        var runs = new List<InlineRun>();
        var context = new TextElementContext(
            new LogicalTextLine(0, 0, editor.Text.Length, editor.Text.Length),
            editor.Text.AsMemory(),
            IdentityTextOffsetMap.Instance);
        foreach (var generator in editor.TextArea.TextView.Extensions.ElementGenerators)
        {
            generator.Generate(in context, runs);
        }
        return runs;
    }

    [TestMethod]
    public void GeneratedElementsBecomeInlineRuns()
    {
        var editor = new TextEditor { Text = "a@b@c" };
        editor.TextArea.TextView.ElementGenerators.Add(new MarkerGenerator("<at>"));

        var runs = Generate(editor);

        Assert.HasCount(2, runs);
        Assert.AreEqual(1, runs[0].Position);
        Assert.AreEqual(1, runs[0].Length);
        Assert.AreEqual(3, runs[1].Position);
    }

    [TestMethod]
    public void DecliningAGeneratorStillAdvancesTheScan()
    {
        var editor = new TextEditor { Text = "@@@" };
        var generator = new DecliningGenerator();
        editor.TextArea.TextView.ElementGenerators.Add(generator);

        var runs = Generate(editor);

        Assert.IsEmpty(runs);
        Assert.IsLessThanOrEqualTo(4, generator.AskedFrom.Count, "A declined offset must not restart the scan.");
    }

    private sealed class DecliningGenerator : VisualLineElementGenerator
    {
        public List<int> AskedFrom { get; } = [];

        public override int GetFirstInterestedOffset(int startOffset)
        {
            AskedFrom.Add(startOffset);
            var line = CurrentContext!.CurrentDocumentLine;
            return startOffset < line.Offset + line.Length ? startOffset : -1;
        }

        public override VisualLineElement? ConstructElement(int offset) => null;
    }

    [TestMethod]
    public void LinkGeneratorReplacesUrls()
    {
        var editor = new TextEditor { Text = "see https://example.com/docs now" };
        editor.TextArea.TextView.ElementGenerators.Add(new LinkElementGenerator());

        var runs = Generate(editor);

        var run = runs.Single();
        Assert.AreEqual(4, run.Position);
        Assert.AreEqual("https://example.com/docs".Length, run.Length);
    }

    [TestMethod]
    public void GeneratorRegistrationInvalidatesTheView()
    {
        var editor = new TextEditor { Text = "x" };
        long before = editor.TextArea.TextView.Extensions.Revision;

        editor.TextArea.TextView.ElementGenerators.Add(new LinkElementGenerator());

        Assert.IsGreaterThan(before, editor.TextArea.TextView.Extensions.Revision);
    }

    [TestMethod]
    public void GenerationContextIsClearedAfterTheLine()
    {
        var editor = new TextEditor { Text = "a@b" };
        var generator = new MarkerGenerator("!");
        editor.TextArea.TextView.ElementGenerators.Add(generator);

        Generate(editor);

        Assert.ThrowsExactly<NullReferenceException>(() => generator.GetFirstInterestedOffset(0),
            "CurrentContext must not outlive generation.");
    }
}
