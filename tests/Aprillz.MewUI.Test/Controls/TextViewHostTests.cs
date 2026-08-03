using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TextViewHostTests
{
    [TestMethod]
    public void MultiLineTextBox_ExposesItsDocumentThroughTheHostContract()
    {
        var box = new MultiLineTextBox { Text = "hello" };
        ITextViewHost host = box;

        Assert.AreSame(box.Document, host.Document);
        Assert.AreSame(box.Extensions, host.Extensions);
        Assert.AreEqual("hello", host.Document.GetText(0, host.Document.TextLength));
    }

    [TestMethod]
    public void MultiLineTextBox_RaisesDocumentChangedOnEveryEdit()
    {
        var box = new MultiLineTextBox();
        ITextViewHost host = box;
        int raised = 0;
        host.DocumentChanged += sender =>
        {
            Assert.AreSame(host, sender);
            raised++;
        };

        box.Document.Insert(0, "abc");
        box.Document.Remove(0, 1);

        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public void SyntaxViewer_RaisesDocumentChangedWhenTextReplacesTheDocument()
    {
        var viewer = new SyntaxViewer();
        ITextViewHost host = viewer;
        int raised = 0;
        IReadOnlyTextDocument? observed = null;
        host.DocumentChanged += sender =>
        {
            observed = sender.Document;
            raised++;
        };

        viewer.Text = "line one\nline two";

        Assert.AreEqual(1, raised);
        Assert.AreSame(viewer.Document, observed);
        Assert.AreEqual(2, viewer.Document.LineCount);
    }
}
