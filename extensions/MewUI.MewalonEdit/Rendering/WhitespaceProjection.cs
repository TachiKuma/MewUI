using Aprillz.MewUI.Text;

namespace ICSharpCode.AvalonEdit.Rendering;

internal sealed class WhitespaceProjection(TextEditorOptions options) : ITextProjection
{
    public ProjectedText Project(in TextProjectionContext context)
    {
        if (!options.ShowSpaces && !options.ShowTabs && !options.ShowEndOfLine)
        {
            return new ProjectedText(context.SourceText, IdentityTextOffsetMap.Instance);
        }

        string source = context.SourceText.ToString();
        char[] text = new char[source.Length + (options.ShowEndOfLine ? 1 : 0)];
        for (int index = 0; index < source.Length; index++)
        {
            text[index] = source[index] switch
            {
                ' ' when options.ShowSpaces => '·',
                '\t' when options.ShowTabs => '→',
                _ => source[index]
            };
        }
        if (options.ShowEndOfLine)
        {
            text[^1] = '¶';
            return new ProjectedText(text.AsMemory(), new EndMarkerOffsetMap(source.Length));
        }
        return new ProjectedText(text.AsMemory(), IdentityTextOffsetMap.Instance);
    }

    private sealed class EndMarkerOffsetMap(int sourceLength) : ITextOffsetMap
    {
        public int MapToSource(int projectedOffset) => Math.Clamp(projectedOffset, 0, sourceLength);
        public int MapFromSource(int sourceOffset) => Math.Clamp(sourceOffset, 0, sourceLength);
    }
}
