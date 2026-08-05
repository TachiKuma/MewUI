namespace Aprillz.MewUI.MewvalonEdit.Document;

/// <summary>Helpers over document text.</summary>
public static class TextUtilities
{
    /// <summary>Whether the string is one of the terminators a document recognises.</summary>
    public static bool IsNewLine(string? newLine)
        => newLine is "\r\n" or "\n" or "\r";

    /// <summary>
    /// The terminator to use when inserting a line break at <paramref name="lineNumber"/>: the one
    /// that line already ends with, or the previous line's at the end of the document. An empty
    /// document has none to copy, so the platform's terminator is used.
    /// </summary>
    public static string GetNewLineFromDocument(TextDocument document, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        var line = document.GetLineByNumber(lineNumber);
        if (line.DelimiterLength == 0)
        {
            if (line.LineNumber <= 1)
            {
                return Environment.NewLine;
            }
            line = document.GetLineByNumber(line.LineNumber - 1);
        }
        return document.GetText(line.Offset + line.Length, line.DelimiterLength);
    }
}
