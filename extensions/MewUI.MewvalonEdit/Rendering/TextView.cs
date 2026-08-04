using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Rendering-side view of the editor, carrying the extension registrations.</summary>
public sealed class TextView(TextArea textArea)
{
    /// <summary>Renderers painting into the known layers, in registration order.</summary>
    public IList<IBackgroundRenderer> BackgroundRenderers => textArea.Editor.BackgroundRenderers;

    /// <summary>Transformers restyling ranges of each visual line.</summary>
    public IList<IVisualLineTransformer> LineTransformers => textArea.Editor.LineTransformers;

    /// <summary>Generators replacing document ranges with elements that draw themselves.</summary>
    public IList<VisualLineElementGenerator> ElementGenerators => textArea.Editor.ElementGenerators;

    /// <summary>Extension pipeline of the editing surface, for MewUI-native extensions.</summary>
    public TextViewExtensionPipeline Extensions => textArea.Editor.Surface.Extensions;

    /// <summary>The editing surface as a text view host, for host-neutral extensions.</summary>
    public ITextViewHost Host => textArea.Editor.Surface;

    /// <summary>Document the view presents.</summary>
    public Document.TextDocument Document => textArea.Editor.Document;

    public string FontFamily
    {
        get => textArea.Editor.FontFamily;
        set => textArea.Editor.FontFamily = value;
    }

    public Color Foreground
    {
        get => textArea.Editor.Foreground;
        set => textArea.Editor.Foreground = value;
    }

    public void Redraw() => textArea.Editor.InvalidateTextView();

    /// <summary>
    /// Lines currently laid out, in document order. Rebuilt from the engine's materialized lines on
    /// each read, so hold one only within a single pass over the view.
    /// </summary>
    public IReadOnlyList<VisualLine> VisualLines
    {
        get
        {
            var host = Host;
            var lines = host.VisibleTextLines;
            var result = new VisualLine[lines.Count];
            for (int index = 0; index < lines.Count; index++)
            {
                result[index] = Wrap(lines[index]);
            }
            return result;
        }
    }

    /// <summary>The laid-out line containing the document line number, or null when not visible.</summary>
    public VisualLine? GetVisualLine(int documentLineNumber)
    {
        foreach (var line in Host.VisibleTextLines)
        {
            if (line.LogicalLine.LineNumber == documentLineNumber - 1)
            {
                return Wrap(line);
            }
        }
        return null;
    }

    private VisualLine Wrap(TextLineLayout line)
        => new(
            line,
            Document.GetLineByOffset(line.LogicalLine.Offset),
            textArea.Editor.ElementGeneratorAdapter.GetScannedElements(line.LogicalLine.Offset));

    internal MultiLineTextBox Surface => textArea.Editor.Surface;
}
