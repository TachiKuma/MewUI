namespace Aprillz.MewUI.Text;

/// <summary>Contract of a control that presents a text document through the extension pipeline.</summary>
public interface ITextViewHost
{
    /// <summary>Document whose text the view presents.</summary>
    IReadOnlyTextDocument Document { get; }

    /// <summary>Extension pipeline applied when visible lines are laid out.</summary>
    TextViewExtensionPipeline Extensions { get; }

    /// <summary>Raised after the document content changed or the document was replaced.</summary>
    event Action<ITextViewHost>? DocumentChanged;

    /// <summary>Re-runs registered classifiers, generators, projections, and adornments.</summary>
    void InvalidateTextView();
}
