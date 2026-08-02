using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Substitutes a visible dot for each space, matching AvalonEdit's SpaceTextElement. Tabs are
/// deliberately not projected: replacing a tab with a glyph collapses its tab-stop width, so tab
/// markers are drawn as adornments over the laid-out tab instead.
/// </summary>
internal sealed class SpaceMarkerProjection(TextEditorOptions options) : ITextProjection
{
    private const char SPACE_MARKER = '·';

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
