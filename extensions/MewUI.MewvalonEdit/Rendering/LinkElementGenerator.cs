using System.Diagnostics;
using System.Text.RegularExpressions;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Link text that opens its target in the system browser. It decorates the document text rather
/// than replacing it, so a link stays editable and the caret still moves inside it.
/// </summary>
public class VisualLineLinkText : VisualLineText
{
    public VisualLineLinkText(int documentLength) : base(documentLength)
    {
    }

    /// <summary>Target opened on click. Mail addresses carry the mailto prefix already.</summary>
    public string NavigateUri { get; set; } = string.Empty;

    /// <summary>Requires Ctrl+Click to follow the link, leaving a plain click for the caret.</summary>
    public bool RequireControlModifierForClick { get; set; } = true;

    protected internal override void PrepareForPaint(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        // Assigned every time, not filled in when empty: the scan cache outlives a colour change,
        // so a value kept from the last paint would never be replaced.
        Foreground = textView.ResolvedLinkTextForeground;
        BackgroundBrush = textView.LinkTextBackgroundBrush;
        TextRunProperties.SetTextDecorations(
            textView.LinkTextUnderline ? TextDecoration.Underline : TextDecoration.None);
    }

    protected internal override void OnQueryCursor(QueryCursorEventArgs e)
    {
        if (!RequireControlModifierForClick || (e.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Cursor = CursorType.Hand;
        }
    }

    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButton.Left || NavigateUri.Length == 0)
        {
            return;
        }
        if (RequireControlModifierForClick && (e.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }
        NavigateTo(NavigateUri);
        e.Handled = true;
    }

    /// <summary>Opens the target. Override to intercept navigation, e.g. for in-app handling.</summary>
    protected virtual void NavigateTo(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (SystemException)
        {
            // No handler for the scheme is the user's configuration, not the editor's failure.
        }
    }
}

/// <summary>Underlines URLs found in the document text.</summary>
public class LinkElementGenerator : VisualLineElementGenerator
{
    public static readonly Regex DefaultLinkRegex =
        new(@"\b(https?://|ftp://|www\.)[\w\d\._/\-~%@()+:?&=#!]*[\w\d/]", RegexOptions.CultureInvariant);

    public static readonly Regex DefaultMailRegex =
        new(@"\b[\w\d\.\-]+@[\w\d\.\-]+\.[a-z]{2,6}\b", RegexOptions.CultureInvariant);

    private readonly Regex _linkRegex;

    public LinkElementGenerator() : this(DefaultLinkRegex)
    {
    }

    public LinkElementGenerator(Regex regex)
        => _linkRegex = regex ?? throw new ArgumentNullException(nameof(regex));

    /// <summary>Requires Ctrl+Click to follow generated links. Default true, as in AvalonEdit.</summary>
    public bool RequireControlModifierForClick { get; set; } = true;

    /// <summary>Builds the link element. Override to substitute a subclass, e.g. one intercepting navigation.</summary>
    protected virtual VisualLineLinkText CreateLinkElement(string text, int documentLength)
        => new(documentLength);

    /// <summary>Target for a matched text. The default treats the match itself as the URI.</summary>
    protected virtual string GetUriFromMatch(Match match)
        => match.Value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + match.Value : match.Value;

    public override int GetFirstInterestedOffset(int startOffset)
        => Match(startOffset, out int matchOffset).Success ? matchOffset : -1;

    public override VisualLineElement? ConstructElement(int offset)
    {
        var match = Match(offset, out int matchOffset);
        if (!match.Success || matchOffset != offset)
        {
            return null;
        }
        var element = CreateLinkElement(match.Value, match.Length);
        element.NavigateUri = GetUriFromMatch(match);
        element.RequireControlModifierForClick = RequireControlModifierForClick;
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
}

/// <summary>Underlines mail addresses and opens them through the mailto scheme.</summary>
public class MailLinkElementGenerator : LinkElementGenerator
{
    public MailLinkElementGenerator() : base(DefaultMailRegex)
    {
    }

    public MailLinkElementGenerator(Regex regex) : base(regex)
    {
    }

    protected override string GetUriFromMatch(Match match) => "mailto:" + match.Value;
}
