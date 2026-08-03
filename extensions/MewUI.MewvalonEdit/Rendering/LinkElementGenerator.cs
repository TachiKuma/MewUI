using System.Text.RegularExpressions;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Underlines URLs found in the document text.</summary>
public class LinkElementGenerator : VisualLineElementGenerator
{
    internal static readonly Regex DefaultLinkRegex =
        new(@"\b(https?://|ftp://|www\.)[\w\d\._/\-~%@()+:?&=#!]*[\w\d/]", RegexOptions.CultureInvariant);

    internal static readonly Regex DefaultMailRegex =
        new(@"\b[\w\d\.\-]+@[\w\d\.\-]+\.[a-z]{2,6}\b", RegexOptions.CultureInvariant);

    private readonly Regex _linkRegex;

    public LinkElementGenerator() : this(DefaultLinkRegex)
    {
    }

    public LinkElementGenerator(Regex regex)
        => _linkRegex = regex ?? throw new ArgumentNullException(nameof(regex));

    /// <summary>Color of the generated link text. Falls back to the document foreground when unset.</summary>
    public Color? LinkColor { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
        => Match(startOffset, out int matchOffset).Success ? matchOffset : -1;

    public override VisualLineElement? ConstructElement(int offset)
    {
        var match = Match(offset, out int matchOffset);
        if (!match.Success || matchOffset != offset)
        {
            return null;
        }
        var element = new TextReplacementElement(match.Value, match.Length, ResolveStyle())
        {
            Foreground = LinkColor
        };
        element.TextRunProperties.SetTextDecorations(TextDecoration.Underline);
        return element;
    }

    private Match Match(int startOffset, out int matchOffset)
    {
        if (CurrentContext is not ITextRunConstructionContext context)
        {
            matchOffset = -1;
            return System.Text.RegularExpressions.Match.Empty;
        }
        var line = context.CurrentDocumentLine;
        int lineEnd = line.Offset + line.Length;
        if (startOffset >= lineEnd)
        {
            matchOffset = -1;
            return System.Text.RegularExpressions.Match.Empty;
        }
        string text = context.Document.GetText(startOffset, lineEnd - startOffset);
        var match = _linkRegex.Match(text);
        matchOffset = match.Success ? startOffset + match.Index : -1;
        return match;
    }

    private TextRunStyle ResolveStyle()
        => (CurrentContext?.DefaultStyle ?? TextRunStyle.Default) with { Decoration = TextDecoration.Underline };
}
