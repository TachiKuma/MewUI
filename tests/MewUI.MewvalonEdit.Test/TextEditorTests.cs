using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace Aprillz.MewUI.MewvalonEdit.Test;

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

    /// <summary>
    /// Without a byte order mark the reader has nothing to detect, so the encoding the caller set is
    /// what decodes the bytes. Hardcoding UTF-8 there turns every other encoding into question marks.
    /// </summary>
    [TestMethod]
    public void LoadDecodesWithTheEncodingItWasGiven()
    {
        byte[] latin1 = System.Text.Encoding.Latin1.GetBytes("café");
        var editor = new TextEditor { Encoding = System.Text.Encoding.Latin1 };

        editor.Load(new MemoryStream(latin1));

        Assert.AreEqual("café", editor.Text);
    }

    [TestMethod]
    public void LoadPrefersAByteOrderMarkOverTheEncodingItWasGiven()
    {
        var utf8WithMark = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        byte[] bytes = [.. utf8WithMark.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes("café")];
        var editor = new TextEditor { Encoding = System.Text.Encoding.Latin1 };

        editor.Load(new MemoryStream(bytes));

        Assert.AreEqual("café", editor.Text);
        Assert.AreEqual(System.Text.Encoding.UTF8.CodePage, editor.Encoding?.CodePage,
            "The encoding ends up at what was actually read.");
    }

    /// <summary>A document saved with no encoding set carries no byte order mark, as in the original.</summary>
    [TestMethod]
    public void SaveWithoutAnEncodingWritesNoByteOrderMark()
    {
        var editor = new TextEditor { Text = "café" };
        var stream = new MemoryStream();

        editor.Save(stream);

        Assert.HasCount(System.Text.Encoding.UTF8.GetBytes("café").Length, stream.ToArray());
    }
}
