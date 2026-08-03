# Text View Extensions

`MultiLineTextBox` and `SyntaxViewer` can be extended without subclassing the control or touching its rendering code. Painting search results, drawing squiggles under error ranges, showing whitespace characters as visible marks, and folding lines away are all added by registering extension objects. The `TextEditor` in the MewvalonEdit extension uses the same pipeline; register through `editor.TextArea.TextView.Extensions`.

## First example: search highlighting

The most common extension is painting a color over a character range. Implement `ITextClassifier` and add it to `Extensions.Classifiers`.

```csharp
using Aprillz.MewUI.Text;

sealed class SearchHighlighter : ITextClassifier
{
    private static readonly Color _matchColor = Color.FromArgb(88, 255, 214, 0);

    public List<int> Matches { get; } = new();   // absolute document offsets
    public int QueryLength { get; set; }

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        int lineStart = context.LogicalLine.Offset;
        int lineEnd = lineStart + context.LogicalLine.Length;

        foreach (int matchStart in Matches)
        {
            if (matchStart >= lineEnd) break;
            int start = Math.Max(lineStart, matchStart);
            int end = Math.Min(lineEnd, matchStart + QueryLength);
            if (end > start)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Background: _matchColor));
            }
        }
    }
}
```

```csharp
var highlighter = new SearchHighlighter();
editor.Extensions.Classifiers.Add(highlighter);

// When the query changes: refill Matches and request a redraw.
highlighter.Matches.Clear();
// ... collect match offsets from the document text ...
editor.InvalidateTextView();
```

`Classify` runs only for lines that are visible on screen, so keep heavy work such as match scanning precomputed and make the callback a lookup. A working example including chevron navigation is the "Find Highlight" card on the gallery Inputs page (`samples/MewUI.Gallery/GalleryView.Input.cs`).

## Choosing an extension

The list to register in depends on what you want to do. All of them live under `Extensions` (`TextViewExtensionPipeline`).

| Goal | Extension | List |
| --- | --- | --- |
| Foreground/background/underline over a character range | `ITextClassifier` | `Classifiers` |
| Arbitrary drawing such as squiggles or bracket highlights | `ITextAdornmentProvider` | `AdornmentProviders` |
| Replacing display text (whitespace marks, folding placeholders) | `ITextProjection` | `Projections` |
| Inserting inline elements into a line | `ITextElementGenerator` | `ElementGenerators` |
| Styles that change glyph layout, such as weight or size | `ITextLineTransformer` | `Transformers` |
| Hiding whole lines from display | `ITextLineCollapser` | `LineCollapsers` |

Use a classifier when only colors change and a transformer only for changes that affect glyph widths and wrapping. Classifier color changes do not re-run layout.

## Colors and decorations: TextPaintSpan

A classifier outputs `TextPaintSpan` values, each a line-relative range with a style bundle.

```csharp
public readonly record struct TextPaintSpan(
    TextRange Range,
    Color? Foreground = null,
    Color? Background = null,
    TextDecoration Decoration = TextDecoration.None);
```

Where spans overlap, the classifier registered later wins. Backgrounds are painted in registration order so later ones sit on top, and later foreground colors override earlier ones. Registration order is also how you prioritize against built-in highlighting.

## Arbitrary drawing: adornments

Shapes that spans cannot express (squiggles, borders, connectors) are drawn by an `ITextAdornmentProvider` that produces `ITextAdornment` objects per line. `Draw` receives the line layout (`TextLineLayout`), so it can query the actual geometry of a character range and draw at exact coordinates.

`Layer` decides the drawing order. A line is drawn as `Background` adornments, glyphs with paint spans, `Text` adornments, then `Foreground` adornments.

- `TextAdornmentLayer.Background`: below glyphs. Block backgrounds, current-line highlight
- `TextAdornmentLayer.Text`: directly above glyphs
- `TextAdornmentLayer.Foreground`: topmost. Squiggles and strikethrough-like decorations

## Replacing display text: projections

An `ITextProjection` changes the display text of a line itself. Use it to substitute tabs/spaces with marks such as `·`, or to shorten a folded code region into a `...` placeholder.

```csharp
public interface ITextProjection
{
    ProjectedText Project(in TextProjectionContext context);
}

public readonly record struct ProjectedText(ReadOnlyMemory<char> Text, ITextOffsetMap OffsetMap);
```

A projection must return an `ITextOffsetMap` along with the text. The map converts between display offsets and document offsets; for 1:1 substitutions that keep the length, return `IdentityTextOffsetMap.Instance` as is.

When a substitution changes the length, display offsets and document offsets diverge. Classifiers and adornments that draw document-offset data (search matches, diagnostic ranges) must convert their ranges with `MapFromSource` (document to display) on the `OffsetMap` passed in their context before producing output. This keeps highlights correct while projections such as folding are active.

To hide whole lines, use `ITextLineCollapser` rather than a projection. A folding feature is typically the combination of a projection that replaces the first line with a placeholder and a collapser that hides the following lines.

## When things are redrawn

Extension callbacks run only when a visible line is laid out: when scrolling reveals new lines, when the document changes, or when `InvalidateTextView()` is called. Two rules follow from this execution model.

- When the document changes, the view refreshes itself. If your extension holds a cache (parse results, match lists), update only that cache on document changes. Both controls provide a `DocumentChanged` event that reports content changes and document replacement, which is the right trigger for cache updates.
- When the extension's own state changes (a new query, replaced highlighting rules, registrations added or removed), the view cannot know, so call `InvalidateTextView()` yourself.

Do not parse the whole document inside a callback. Parse once when the document changes, keep the result, and make the callback a lookup of the part intersecting the line.

Assigning a new document to `MultiLineTextBox.Document` keeps the extension registrations and the view (caret, selection, scroll, and undo reset), so there is no need to re-register extensions on document replacement.
