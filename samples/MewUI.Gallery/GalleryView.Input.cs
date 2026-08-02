using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ObservableValue<string> name = new ObservableValue<string>("This is my name");
    private ObservableValue<int> intBinding = new ObservableValue<int>(1);
    private ObservableValue<double> doubleBinding = new ObservableValue<double>(42.5);

    // Multi-line text box demo that shows the live selection (start / length) bound to the read-only
    // SelectionStart/SelectionLength MewProperties - used to inspect selection geometry.
    private FrameworkElement MultiLineTextBoxDemo()
    {
        var box = new MultiLineTextBox()
            .Height(120)
            .Width(290)
            .Wrap(false)
            .Text("The quick brown fox jumps over the lazy dog, then keeps running far beyond the visible editor width.\n\n- Wrap supported\n- Selection supported\n- Scroll supported");

        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new CheckBox()
                    .Content("Wrap")
                    .IsChecked(box.Wrap)
                    .OnCheckedChanged(isChecked => box.Wrap = isChecked == true),
                box,
                new TextBlock()
                    .FontSize(11)
                    .Bind(TextBlock.TextProperty, box, TextBase.SelectionStartProperty,
                        (int start) => $"SelectionStart: {start}"),
                new TextBlock()
                    .FontSize(11)
                    .Bind(TextBlock.TextProperty, box, TextBase.SelectionLengthProperty,
                        (int length) => $"SelectionLength: {length}")
            );
    }

    private FrameworkElement SyntaxViewerDemo()
    {
        var viewer = new SyntaxViewer
        {
            Width = 620,
            Height = 330,
            Wrap = false,
            FontFamily = "Consolas",
            Text = """
                using System.Collections.Generic;
                using System.Linq;

                namespace Gallery.Syntax;

                [Obsolete("Use CreateAsync instead")]
                public sealed record Result(int Id, string Name);

                public static class ResultService
                {
                    // Keywords, types, numbers, members, strings, and interpolation.
                    public static async Task<IReadOnlyList<Result>> CreateAsync(
                        IEnumerable<string?> names,
                        CancellationToken cancellationToken = default)
                    {
                        const int minimumLength = 3;
                        await Task.Delay(42, cancellationToken);

                        return names
                            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length >= minimumLength)
                            .Select((name, index) => new Result(index + 1, $"Item {index}: {name!.Trim()}"))
                            .ToArray();
                    }
                }
                """
        };
        viewer.Extensions.Classifiers.Add(new GalleryCSharpClassifier());
        viewer.InvalidateTextView();
        return viewer;
    }

    private FrameworkElement InputsPage() =>
            CardGrid(
                Card(
                    "TextBox",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBox(),
                            new TextBox().Placeholder("Type your name..."),
                            new TextBox().BindText(name),
                            new TextBox().Text("Disabled").Disable()
                        )
                ),

                Card(
                    "PasswordBox",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new PasswordBox().Placeholder("Password"),
                            new PasswordBox { PasswordChar = '★' }.Placeholder("Custom mask"),
                            new PasswordBox().Password("Disabled").Disable()
                        )
                ),

                Card(
                    "NumericUpDown (int/double)",
                    new Grid()
                        .Columns("Auto,Auto,Auto")
                        .Rows("Auto,Auto")
                        .Spacing(8)
                        .AutoIndexing()
                        .Children(
                            new TextBlock()
                                .Text("Int")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(1)
                                .Format("0")
                                .BindValue(intBinding)
                                .CenterVertical(),

                            new TextBlock()
                                .BindText(intBinding, value => $"Value: {value}")
                                .CenterVertical(),

                            new TextBlock()
                                .Text("Double")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(0.1)
                                .Format("0.##")
                                .BindValue(doubleBinding)
                                .CenterVertical(),

                            new TextBlock()
                                .BindText(doubleBinding, value => $"Value: {value:0.##}")
                                .CenterVertical()
                        )
                ),

                Card(
                    "Emoji",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(24),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(20),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(16),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(12),
                            new TextBox()
                                .Placeholder("Type or paste emoji here...")
                                .Text("\U0001F36B\U0001F600\U0001F389"),
                            new TextBlock()
                                .Text("Mixed: Hello \U0001F30D World \U0001F680!")
                                .FontSize(14)
                        )
                ),

                Card(
                    "MultiLineTextBox",
                    MultiLineTextBoxDemo()
                ),

                Card(
                    "SyntaxViewer",
                    SyntaxViewerDemo(),
                    minWidth: 650
                ),

                Card(
                    "ToolTip / ContextMenu",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Text("Hover to show a tooltip. Right-click to open a context menu.")
                                .TextWrapping(TextWrapping.Wrap)
                                .Width(290)
                                .FontSize(11),

                            new Button()
                                .Content("Hover / Right-click me")
                                .ToolTip("ToolTip text")
                                .ContextMenu(
                                    new ContextMenu()
                                        .Item("Copy", new KeyGesture(Key.C, ModifierKeys.Primary))
                                        .Item("Paste", new KeyGesture(Key.V, ModifierKeys.Primary))
                                        .Separator()
                                        .SubMenu("Transform", new ContextMenu()
                                            .Item("Uppercase")
                                            .Item("Lowercase")
                                            .Separator()
                                            .SubMenu("More", new ContextMenu()
                                                .Item("Trim")
                                                .Item("Normalize")
                                                .Item("Sort"))
                                        )
                                        .SubMenu("View", new ContextMenu()
                                            .Item("Zoom In", new KeyGesture(Key.Add, ModifierKeys.Primary))
                                            .Item("Zoom Out", new KeyGesture(Key.Subtract, ModifierKeys.Primary))
                                            .Item("Reset Zoom", new KeyGesture(Key.D0, ModifierKeys.Primary))
                                        )
                                        .Separator()
                                        .Item("Disabled", isEnabled: false)
                                )
                         )
                 )
             );

    private sealed class GalleryCSharpClassifier : ITextClassifier
    {
        private static readonly HashSet<string> Keywords =
        [
            "async", "await", "class", "const", "default", "false", "namespace", "new", "null",
            "public", "record", "return", "sealed", "static", "true", "using"
        ];

        private static readonly HashSet<string> BuiltInTypes =
            ["bool", "double", "int", "object", "string", "var", "void"];

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            ReadOnlySpan<char> text = context.Text.Span;
            int index = 0;
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                    continue;
                }

                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
                {
                    Add(output, index, text.Length - index, "#6A9955");
                    break;
                }

                int stringPrefix = text[index] == '$' && index + 1 < text.Length && text[index + 1] == '"' ? 1 : 0;
                if (text[index + stringPrefix] is '"' or '\'')
                {
                    char delimiter = text[index + stringPrefix];
                    int end = index + stringPrefix + 1;
                    while (end < text.Length)
                    {
                        if (text[end] == '\\')
                        {
                            end = Math.Min(text.Length, end + 2);
                            continue;
                        }
                        if (text[end++] == delimiter) break;
                    }
                    Add(output, index, end - index, "#CE9178");
                    index = end;
                    continue;
                }

                if (char.IsDigit(text[index]))
                {
                    int end = index + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '.' or '_')) end++;
                    Add(output, index, end - index, "#B5CEA8");
                    index = end;
                    continue;
                }

                if (char.IsLetter(text[index]) || text[index] == '_')
                {
                    int end = index + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
                    string identifier = text[index..end].ToString();
                    if (Keywords.Contains(identifier) || BuiltInTypes.Contains(identifier))
                        Add(output, index, end - index, "#569CD6");
                    else if (char.IsUpper(identifier[0]))
                        Add(output, index, end - index, "#4EC9B0");
                    else if (PreviousNonWhitespace(text, index) == '.')
                        Add(output, index, end - index, "#DCDCAA");
                    index = end;
                    continue;
                }

                index++;
            }
        }

        private static char PreviousNonWhitespace(ReadOnlySpan<char> text, int index)
        {
            for (int current = index - 1; current >= 0; current--)
            {
                if (!char.IsWhiteSpace(text[current])) return text[current];
            }
            return '\0';
        }

        private static void Add(IList<TextPaintSpan> output, int start, int length, string color)
            => output.Add(new TextPaintSpan(
                new TextRange(start, length),
                Foreground: Color.FromHex(color)));
    }
}
