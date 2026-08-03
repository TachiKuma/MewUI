using System.Collections;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>List that reports mutations so the view can repaint.</summary>
internal sealed class ExtensionList<T>(Action onChanged) : IList<T>
{
    private readonly List<T> _items = [];

    public T this[int index]
    {
        get => _items[index];
        set { _items[index] = value; onChanged(); }
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(T item) { _items.Add(item); onChanged(); }
    public void Insert(int index, T item) { _items.Insert(index, item); onChanged(); }
    public bool Remove(T item) { bool removed = _items.Remove(item); if (removed) onChanged(); return removed; }
    public void RemoveAt(int index) { _items.RemoveAt(index); onChanged(); }
    public void Clear() { _items.Clear(); onChanged(); }
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public int IndexOf(T item) => _items.IndexOf(item);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Runs the registered <see cref="IVisualLineTransformer"/>s and translates their element overrides
/// into engine paint spans and geometry runs. Registered as both a classifier and a transformer
/// because colors and fonts travel through different pipeline stages; the per-line result is
/// computed once and shared between the two calls.
/// </summary>
internal sealed class LineTransformerAdapter(TextEditor editor) : ITextClassifier, ITextLineTransformer
{
    private readonly List<VisualLineElement> _elements = [];
    private readonly TransformContext _context = new(editor);
    private long _cachedVersion = -1;
    private int _cachedOffset = -1;
    private int _cachedLength = -1;

    public IList<IVisualLineTransformer> Transformers { get; } =
        new ExtensionList<IVisualLineTransformer>(editor.InvalidateTextView);

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        EnsureComputed(context.LogicalLine);
        foreach (var element in _elements)
        {
            var properties = element.TextRunProperties;
            var background = element.BackgroundBrush ?? properties.BackgroundBrush;
            if (!properties.HasPaint && !background.HasValue)
            {
                continue;
            }
            output.Add(new TextPaintSpan(
                new TextRange(element.RelativeTextOffset, element.DocumentLength),
                properties.ForegroundBrush,
                background,
                properties.TextDecorations));
        }
    }

    public void Transform(
        in TextLineTransformContext context,
        IList<GeometryStyleRun> geometryRuns,
        IList<InlineRun> inlines)
    {
        EnsureComputed(context.LogicalLine);
        foreach (var element in _elements)
        {
            var properties = element.TextRunProperties;
            if (!properties.HasFont)
            {
                continue;
            }
            var style = context.DefaultStyle with
            {
                FontFamily = properties.FontFamily ?? context.DefaultStyle.FontFamily,
                FontSize = properties.FontRenderingEmSize ?? context.DefaultStyle.FontSize,
                Weight = properties.FontWeight ?? context.DefaultStyle.Weight,
                Italic = properties.Italic ?? context.DefaultStyle.Italic
            };
            geometryRuns.Add(new GeometryStyleRun(element.RelativeTextOffset, element.DocumentLength, style));
        }
    }

    private void EnsureComputed(in LogicalTextLine logical)
    {
        long version = editor.Document.Version;
        if (_cachedVersion == version && _cachedOffset == logical.Offset && _cachedLength == logical.Length)
        {
            return;
        }
        _cachedVersion = version;
        _cachedOffset = logical.Offset;
        _cachedLength = logical.Length;
        _elements.Clear();
        if (Transformers.Count == 0)
        {
            return;
        }

        _context.CurrentDocumentLine = editor.Document.GetLineByOffset(logical.Offset);
        foreach (var transformer in Transformers)
        {
            transformer.Transform(_context, _elements);
        }
    }

    private sealed class TransformContext(TextEditor editor) : ITextRunConstructionContext
    {
        public TextDocument Document => editor.Document;
        public DocumentLine CurrentDocumentLine { get; set; } = null!;
    }
}

/// <summary>
/// Bridges <see cref="IBackgroundRenderer"/> onto per-line adornments. Each renderer is invoked once
/// per visible line with the surface clipped to that line, so the layered draw order holds while the
/// renderer still computes geometry for the whole viewport as it does in AvalonEdit.
/// </summary>
internal sealed class BackgroundRendererAdornmentProvider(TextEditor editor) : ITextAdornmentProvider
{
    public IList<IBackgroundRenderer> Renderers { get; } =
        new ExtensionList<IBackgroundRenderer>(editor.InvalidateTextView);

    public void GetAdornments(in TextAdornmentContext context, IList<ITextAdornment> output)
    {
        foreach (var renderer in Renderers)
        {
            output.Add(new BackgroundRendererAdornment(renderer, editor));
        }
    }

    private sealed class BackgroundRendererAdornment(IBackgroundRenderer renderer, TextEditor editor) : ITextAdornment
    {
        public TextAdornmentLayer Layer => renderer.Layer switch
        {
            KnownLayer.Background => TextAdornmentLayer.Background,
            KnownLayer.Selection => TextAdornmentLayer.Selection,
            KnownLayer.Caret => TextAdornmentLayer.Caret,
            _ => TextAdornmentLayer.Text
        };

        public void Draw(ITextRenderContext context, TextLineLayout line, Point origin)
        {
            var graphics = context.Graphics;
            var viewport = editor.Surface.TextViewportBounds;
            graphics.Save();
            try
            {
                graphics.SetClip(new Rect(viewport.X, origin.Y, viewport.Width, line.Height));
                renderer.Draw(editor.TextArea.TextView, graphics);
            }
            finally
            {
                graphics.Restore();
            }
        }
    }
}
