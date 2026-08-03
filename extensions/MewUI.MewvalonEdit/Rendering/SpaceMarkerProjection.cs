using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Substitutes a visible dot for each space, matching AvalonEdit's SpaceTextElement. Tabs are
/// deliberately not projected: replacing a tab with a glyph collapses its tab-stop width, so tab
/// markers are drawn as adornments over the laid-out tab instead.
/// </summary>
internal sealed class SpaceMarkerProjection(TextEditorOptions options) : ITextProjection
{
    internal const char SPACE_MARKER = '·';

    public ProjectedText Project(in TextProjectionContext context)
    {
        var source = context.SourceText;
        if (!options.ShowSpaces || source.Span.IndexOf(' ') < 0)
        {
            return new ProjectedText(source, IdentityTextOffsetMap.Instance);
        }

        char[] projected = source.ToArray();
        for (int index = 0; index < projected.Length; index++)
        {
            if (projected[index] == ' ')
            {
                projected[index] = SPACE_MARKER;
            }
        }
        return new ProjectedText(projected.AsMemory(), IdentityTextOffsetMap.Instance);
    }
}

/// <summary>
/// Paints the substituted space markers in the marker color, matching the tab and end-of-line
/// adornments. Without this they would inherit the document foreground and read as real content.
/// </summary>
internal sealed class SpaceMarkerClassifier(TextEditorOptions options, TextEditor editor) : ITextClassifier
{
    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        if (!options.ShowSpaces)
        {
            return;
        }

        // Scanning the projected text keeps the offsets valid when a later projection (folding)
        // changes the line length.
        var text = context.Text.Span;
        var color = editor.WhitespaceMarkerColor;
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] != SpaceMarkerProjection.SPACE_MARKER)
            {
                index++;
                continue;
            }
            int start = index;
            while (index < text.Length && text[index] == SpaceMarkerProjection.SPACE_MARKER)
            {
                index++;
            }
            output.Add(new TextPaintSpan(new TextRange(start, index - start), color));
        }
    }
}
