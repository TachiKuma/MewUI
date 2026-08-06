using System.ComponentModel;
using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Test;

/// <summary>
/// What a document tells a binding about itself. The order is fixed in the original: Text first,
/// then the two derived counts, and those only when they actually moved.
/// </summary>
[TestClass]
public sealed class DocumentNotificationTests
{
    [TestMethod]
    public void AChangeReportsTextThenTheCountsThatMoved()
    {
        var document = new TextDocument("one");
        var raised = new List<string?>();
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        document.Insert(3, "\ntwo");

        CollectionAssert.AreEqual(
            new[] { nameof(TextDocument.Text), nameof(TextDocument.TextLength), nameof(TextDocument.LineCount) },
            raised);
    }

    [TestMethod]
    public void ACountThatDidNotMoveIsNotReported()
    {
        var document = new TextDocument("one");
        var raised = new List<string?>();
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Same length, same line count: only the text itself changed.
        document.Replace(0, 3, "two");

        CollectionAssert.AreEqual(new[] { nameof(TextDocument.Text) }, raised);
    }

    [TestMethod]
    public void TheFileNameRaisesOnlyWhenItActuallyChanges()
    {
        var document = new TextDocument("text");
        int raised = 0;
        document.FileNameChanged += (_, _) => raised++;

        document.FileName = "a.cs";
        document.FileName = "a.cs";

        Assert.AreEqual("a.cs", document.FileName);
        Assert.AreEqual(1, raised);
    }
}
