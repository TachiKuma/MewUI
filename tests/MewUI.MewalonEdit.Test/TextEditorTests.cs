using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;

namespace MewUI.MewalonEdit.Test;

[TestClass]
public sealed class TextEditorTests
{
    [TestMethod]
    public void EditorConsumesDocumentAndExtensionPipeline()
    {
        var document = new TextDocument("class C\n{\n}\n");
        var editor = new TextEditor
        {
            Document = document,
            SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"),
            ShowLineNumbers = true
        };
        var folding = FoldingManager.Install(editor);
        folding.UpdateFoldings([new NewFolding(7, document.TextLength - 1) { DefaultClosed = true }], -1);

        Assert.AreSame(document, editor.Document);
        Assert.AreEqual(document.Text, editor.Text);
        Assert.IsTrue(editor.ShowLineNumbers);
        Assert.IsTrue(folding.AllFoldings.Single().IsFolded);

        document.Insert(document.TextLength, "// end");
        Assert.EndsWith("// end", document.Text);
        FoldingManager.Uninstall(folding);
    }
}
