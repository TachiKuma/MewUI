using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Indentation;

public class DefaultIndentationStrategy : IIndentationStrategy
{
    public virtual void IndentLine(TextDocument document, DocumentLine line)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(line);
        if (line.LineNumber <= 1) return;

        var previous = document.GetLineByNumber(line.LineNumber - 1);
        string previousText = document.GetText(previous.Offset, previous.Length);
        string currentText = document.GetText(line.Offset, line.Length);
        string indentation = TakeIndentation(previousText);
        int currentIndentationLength = TakeIndentation(currentText).Length;
        document.Replace(line.Offset, currentIndentationLength, indentation);
    }

    public virtual void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (beginLine <= 0 || endLine < beginLine || endLine > document.LineCount)
            throw new ArgumentOutOfRangeException(nameof(beginLine));

        for (int lineNumber = beginLine; lineNumber <= endLine; lineNumber++)
            IndentLine(document, document.GetLineByNumber(lineNumber));
    }

    private static string TakeIndentation(string text)
    {
        int length = 0;
        while (length < text.Length && (text[length] == ' ' || text[length] == '\t')) length++;
        return text[..length];
    }
}
