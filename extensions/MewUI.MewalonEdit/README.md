# MewalonEdit

MewalonEdit provides an AvalonEdit compatibility-oriented editor surface for MewUI. It uses
`Aprillz.MewUI.Text` for layout, hit testing, viewport virtualization, syntax
classification, projections, and adornments, and uses `NewMultiLineTextBox` for
the editing baseline.

The compatibility surface includes `TextEditor`, `TextDocument`, highlighting
definitions, line numbers, visual-line folding, whitespace markers, search,
indentation strategies, and completion sessions. The implementation is
native MewUI code; the AvalonEdit source is used as an API and behavior
reference under its MIT license.

```csharp
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

var editor = new TextEditor
{
    ShowLineNumbers = true,
    SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"),
    Text = "public sealed class Example { }"
};
```
