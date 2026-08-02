using Aprillz.MewUI.Text;
using ICSharpCode.AvalonEdit.Highlighting;

namespace MewUI.MewalonEdit.Test;

[TestClass]
public sealed class HighlightingTests
{
    [TestMethod]
    public void BuiltInCSharpDefinitionClassifiesKeywordsAndStrings()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var classifier = new HighlightingColorizer(definition);
        var output = new List<TextPaintSpan>();
        var text = "public string Value = \"text\";".AsMemory();

        classifier.Classify(new TextClassificationContext(
            new LogicalTextLine(0, 0, text.Length, text.Length), text), output);

        Assert.IsTrue(output.Any(span => span.Range.Start == 0 && span.Range.Length == 6));
        Assert.IsTrue(output.Any(span => span.Range.Start == 7 && span.Range.Length == 6));
        Assert.IsTrue(output.Any(span => span.Range.Length == 6 && span.Range.Start > 7));
    }
}
