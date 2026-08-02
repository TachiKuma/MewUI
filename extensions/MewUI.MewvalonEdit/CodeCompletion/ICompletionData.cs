using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

public interface ICompletionData
{
    string Text { get; }
    object? Content { get; }
    object? Description { get; }
    double Priority { get; }
    void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs);
}

public class CompletionData : ICompletionData
{
    public CompletionData(string text, object? description = null, double priority = 0)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Description = description;
        Priority = priority;
    }

    public string Text { get; }
    public virtual object Content => Text;
    public object? Description { get; }
    public double Priority { get; }

    public virtual void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        ArgumentNullException.ThrowIfNull(completionSegment);
        textArea.Document.Replace(completionSegment.Offset, completionSegment.Length, Text);
        textArea.Editor.Select(completionSegment.Offset + Text.Length, 0);
    }
}
