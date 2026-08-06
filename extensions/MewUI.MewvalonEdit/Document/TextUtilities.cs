using System.Globalization;

namespace Aprillz.MewUI.MewvalonEdit.Document;

/// <summary>Helpers over document text.</summary>
public static class TextUtilities
{
    // The first 32 ASCII characters, the Unicode C0 block.
    private static readonly string[] _c0Names =
    [
        "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT",
        "LF", "VT", "FF", "CR", "SO", "SI", "DLE", "DC1", "DC2", "DC3",
        "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS",
        "RS", "US"
    ];

    // DEL, then the C1 block from 128 to 159.
    private static readonly string[] _delAndC1Names =
    [
        "DEL",
        "PAD", "HOP", "BPH", "NBH", "IND", "NEL", "SSA", "ESA", "HTS", "HTJ",
        "VTS", "PLD", "PLU", "RI", "SS2", "SS3", "DCS", "PU1", "PU2", "STS",
        "CCH", "MW", "SPA", "EPA", "SOS", "SGCI", "SCI", "CSI", "ST", "OSC",
        "PM", "APC"
    ];

    /// <summary>Whether the string is one of the terminators a document recognises.</summary>
    public static bool IsNewLine(string? newLine)
        => newLine is "\r\n" or "\n" or "\r";

    /// <summary>
    /// Short name of a control character, as drawn in the box that stands in for it. An unnamed one
    /// gives its code point as four hex digits.
    /// </summary>
    public static string GetControlCharacterName(char controlCharacter)
    {
        int code = controlCharacter;
        if (code < _c0Names.Length)
        {
            return _c0Names[code];
        }
        return code is >= 127 and <= 159
            ? _delAndC1Names[code - 127]
            : code.ToString("x4", CultureInfo.InvariantCulture);
    }

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
