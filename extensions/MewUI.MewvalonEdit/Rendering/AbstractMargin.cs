using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Base for anything placed beside the text, such as line numbers or a folding gutter. A margin is
/// a control, so it takes input; only the view layers are draw-only.
/// </summary>
public abstract class AbstractMargin : Control, ITextViewConnect
{
    private TextView? _textView;

    /// <summary>View this margin sits beside. Setting it runs the connect and disconnect hooks.</summary>
    public TextView? TextView
    {
        get => _textView;
        set
        {
            if (ReferenceEquals(_textView, value))
            {
                return;
            }

            var old = _textView;
            if (old is not null)
            {
                old.Host.LinesChanged -= OnHostLinesChanged;
                old.Host.ScrollOffsetChanged -= OnHostScrolled;
                RemoveFromTextView(old);
            }

            _textView = value;
            var oldDocument = old?.Document;
            if (value is not null)
            {
                value.Host.LinesChanged += OnHostLinesChanged;
                value.Host.ScrollOffsetChanged += OnHostScrolled;
                AddToTextView(value);
            }

            OnTextViewChanged(old, value);
            if (!ReferenceEquals(oldDocument, Document))
            {
                OnDocumentChanged(oldDocument, Document);
            }
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Document behind <see cref="TextView"/>, or null while unattached.</summary>
    public TextDocument? Document => _textView?.Document;

    /// <summary>Called after <see cref="TextView"/> changed.</summary>
    protected virtual void OnTextViewChanged(TextView? oldValue, TextView? newValue)
    {
    }

    /// <summary>Called after the attached view's document changed.</summary>
    protected virtual void OnDocumentChanged(TextDocument? oldValue, TextDocument? newValue)
    {
    }

    void ITextViewConnect.AddToTextView(TextView textView) => AddToTextView(textView);

    void ITextViewConnect.RemoveFromTextView(TextView textView) => RemoveFromTextView(textView);

    /// <summary>Called while attaching, before <see cref="OnTextViewChanged"/>.</summary>
    protected virtual void AddToTextView(TextView textView)
    {
    }

    /// <summary>Called while detaching, before <see cref="OnTextViewChanged"/>.</summary>
    protected virtual void RemoveFromTextView(TextView textView)
    {
    }

    protected sealed override void OnRender(IGraphicsContext context)
    {
        OnRenderMargin(context);
        if (_textView is null)
        {
            return;
        }

        var textViewport = _textView.Host.TextViewportBounds;
        var clip = Bounds.Intersect(new Rect(Bounds.X, textViewport.Y, Bounds.Width, textViewport.Height));
        if (clip.IsEmpty)
        {
            return;
        }

        context.Save();
        try
        {
            context.SetClip(LayoutRounding.MakeClipRect(clip, GetDpi() / 96.0));
            OnRenderTextViewport(context, textViewport);
        }
        finally
        {
            context.Restore();
        }
    }

    /// <summary>Draws fixed margin chrome outside the scrolling viewport clip.</summary>
    protected virtual void OnRenderMargin(IGraphicsContext context)
    {
    }

    /// <summary>Draws content clipped to this margin's share of the text viewport.</summary>
    protected abstract void OnRenderTextViewport(IGraphicsContext context, Rect textViewport);

    private void OnHostLinesChanged(Aprillz.MewUI.Text.ITextViewHost host) => InvalidateVisual();

    private void OnHostScrolled(Aprillz.MewUI.Text.ITextViewHost host) => InvalidateVisual();
}
