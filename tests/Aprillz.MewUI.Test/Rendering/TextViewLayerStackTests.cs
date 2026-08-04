using Aprillz.MewUI;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Anchors are positions, not slots that own their content: a layer inserted against one draws
/// beside it, and replacing one stops the host from painting that anchor at all.
/// </summary>
[TestClass]
public sealed class TextViewLayerStackTests
{
    [TestMethod]
    public void BuiltInAnchorsDrawInOrder()
    {
        var order = new List<string>();
        var stack = new TextViewLayerStack();

        stack.Draw(NullRenderContext.Instance, default, anchor => order.Add(anchor.ToString()));

        Assert.AreEqual("Background,Selection,Text,Caret", string.Join(',', order));
    }

    [TestMethod]
    public void InsertedLayersSitBesideTheirAnchor()
    {
        var order = new List<string>();
        var stack = new TextViewLayerStack();
        stack.Insert(new RecordingLayer("under", order), TextAdornmentLayer.Text, TextLayerPosition.Below);
        stack.Insert(new RecordingLayer("over", order), TextAdornmentLayer.Text, TextLayerPosition.Above);

        stack.Draw(NullRenderContext.Instance, default, anchor => order.Add(anchor.ToString()));

        Assert.AreEqual("Background,Selection,under,Text,over,Caret", string.Join(',', order));
    }

    [TestMethod]
    public void ReplacingAnAnchorTakesOverItsDrawing()
    {
        var order = new List<string>();
        var stack = new TextViewLayerStack();
        stack.Insert(new RecordingLayer("mine", order), TextAdornmentLayer.Selection, TextLayerPosition.Replace);

        stack.Draw(NullRenderContext.Instance, default, anchor => order.Add(anchor.ToString()));

        Assert.AreEqual("Background,mine,Text,Caret", string.Join(',', order));
        Assert.IsFalse(stack.DrawsOwnContent(TextAdornmentLayer.Selection));
        Assert.IsTrue(stack.DrawsOwnContent(TextAdornmentLayer.Text));
    }

    [TestMethod]
    public void AReplacedAnchorStillAcceptsNeighbours()
    {
        var order = new List<string>();
        var stack = new TextViewLayerStack();
        stack.Insert(new RecordingLayer("mine", order), TextAdornmentLayer.Selection, TextLayerPosition.Replace);
        stack.Insert(new RecordingLayer("under", order), TextAdornmentLayer.Selection, TextLayerPosition.Below);

        stack.Draw(NullRenderContext.Instance, default, anchor => order.Add(anchor.ToString()));

        Assert.AreEqual("Background,under,mine,Text,Caret", string.Join(',', order));
    }

    private sealed class RecordingLayer(string name, List<string> order) : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds) => order.Add(name);
    }

    /// <summary>The stack only forwards the context, so the layers under test never touch it.</summary>
    private sealed class NullRenderContext : ITextRenderContext
    {
        public static NullRenderContext Instance { get; } = new();

        public Aprillz.MewUI.Rendering.IGraphicsContext Graphics => throw new NotSupportedException();

        public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) { }

        public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options) { }

        public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options) { }
    }
}
