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

    /// <summary>Extension pipeline of the editing surface, for MewUI-native extensions.</summary>
    public TextViewExtensionPipeline Extensions => textArea.Editor.Surface.Extensions;

    /// <summary>The editing surface as a text view host, for host-neutral extensions.</summary>
    public ITextViewHost Host => textArea.Editor.Surface;

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

    internal MultiLineTextBox Surface => textArea.Editor.Surface;
}
