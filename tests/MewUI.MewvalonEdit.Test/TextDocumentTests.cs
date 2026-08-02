using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Test;

[TestClass]
public sealed class TextDocumentTests
{
    [TestMethod]
    public void DocumentUsesAvalonEditOneBasedLocations()
    {
        var document = new TextDocument("one\ntwo");

        Assert.AreEqual(2, document.LineCount);
        Assert.AreEqual(4, document.GetLineByNumber(2).Offset);
        Assert.AreEqual(new TextLocation(2, 2), document.GetLocation(5));
        Assert.AreEqual(5, document.GetOffset(2, 2));
    }

    [TestMethod]
    public void MutationsRaiseDocumentAndTextEvents()
    {
        var document = new TextDocument("abc");
        DocumentChangeEventArgs? change = null;
        int textChanges = 0;
        document.Changed += (_, args) => change = args;
        document.TextChanged += (_, _) => textChanges++;

        document.Replace(1, 1, "XYZ");

        Assert.AreEqual("aXYZc", document.Text);
        Assert.IsNotNull(change);
        Assert.AreEqual(1, change.Offset);
        Assert.AreEqual(1, change.RemovalLength);
        Assert.AreEqual(3, change.InsertionLength);
        Assert.AreEqual(1, textChanges);
    }
}
