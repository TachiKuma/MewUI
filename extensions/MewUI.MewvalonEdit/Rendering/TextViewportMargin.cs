using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Base for editor margins whose viewport content follows the text surface.</summary>
internal abstract class TextViewportMargin(TextEditor editor) : Control
{
    protected TextEditor Editor => editor;

    protected sealed override void OnRender(IGraphicsContext context)
    {
        OnRenderMargin(context);

        var textViewport = editor.Surface.TextViewportBounds;
        var clip = Bounds.Intersect(new Rect(Bounds.X, textViewport.Y, Bounds.Width, textViewport.Height));
        if (clip.IsEmpty) return;

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
}
