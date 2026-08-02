using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement TypographyPage()
    {
        var runDemo = new SyntaxViewer
        {
            Width = 620,
            Height = 112,
            Wrap = true,
            Text = "Normal text, bold text, italic text, accent text, and underlined text.\nMixed fonts: Segoe UI + Consolas + 22 pt."
        };
        var runStyler = new RunLikeTextStyler();
        runDemo.Extensions.Classifiers.Add(runStyler);
        runDemo.Extensions.Transformers.Add(runStyler);
        runDemo.InvalidateTextView();

        // Font Inheritance: Border sets FontSize=16, children inherit
        var inheritanceDemo = new Border()
            .FontSize(16)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited 16pt (from parent Border)"),
                        new TextBlock().Text("Also inherited 16pt"),
                        new TextBlock().Text("Override: 10pt").FontSize(10),
                        new Button().Content("Button (inherited 16pt)"),
                        new TextBox().Placeholder("TextBox (inherited 16pt)")
                    ));

        // FontFamily Inheritance
        var fontFamilyDemo = new Border()
            .FontFamily("Consolas")
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited Consolas"),
                        new TextBlock().Text("Also Consolas"),
                        new TextBlock().Text("Override: Segoe UI").FontFamily("Segoe UI"),
                        new Button().Content("Consolas Button")
                    ));

        // FontWeight Inheritance
        var fontWeightDemo = new Border()
            .Bold()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited Bold"),
                        new TextBlock().Text("Also Bold"),
                        new TextBlock().Text("Override: Normal").FontWeight(FontWeight.Normal),
                        new Button().Content("Bold Button")
                    ));

        // Nested inheritance: outer=20pt, inner=12pt
        var nestedDemo = new Border()
            .FontSize(20)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("20pt (from outer)"),
                        new Border()
                            .FontSize(12)
                            .Padding(8)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(4)
                                    .Children(
                                        new TextBlock().Text("12pt (from inner Border)"),
                                        new TextBlock().Text("Also 12pt")
                                    )),
                        new TextBlock().Text("Back to 20pt")
                    ));

        return CardGrid(
            Card(
                "Run-like Inline Text",
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        runDemo,
                        new TextBlock()
                            .FontSize(11)
                            .Text("One logical text surface with per-range color, weight, italic, decoration, font, and size.")),
                minWidth: 650),
            Card("Font Size Inheritance", inheritanceDemo),
            Card("Font Family Inheritance", fontFamilyDemo),
            Card("Font Weight Inheritance", fontWeightDemo),
            Card("Nested Inheritance", nestedDemo)
        );
    }

    private sealed class RunLikeTextStyler : ITextClassifier, ITextLineTransformer
    {
        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            AddPaint(context.Text.Span, "accent text", output, Color.FromHex("#D83B01"));
        }

        public void Transform(
            in TextLineTransformContext context,
            IList<GeometryStyleRun> geometryRuns,
            IList<InlineRun> inlines)
        {
            AddGeometry(context.Text.Span, "bold text", geometryRuns,
                context.DefaultStyle with { Weight = FontWeight.Bold });
            AddGeometry(context.Text.Span, "italic text", geometryRuns,
                context.DefaultStyle with { Italic = true });
            AddGeometry(context.Text.Span, "underlined text", geometryRuns,
                context.DefaultStyle with { Decoration = TextDecoration.Underline });
            AddGeometry(context.Text.Span, "Consolas", geometryRuns,
                context.DefaultStyle with { FontFamily = "Consolas" });
            AddGeometry(context.Text.Span, "22 pt", geometryRuns,
                context.DefaultStyle with { FontSize = 22 });
        }

        private static void AddPaint(
            ReadOnlySpan<char> text,
            string value,
            IList<TextPaintSpan> output,
            Color foreground)
        {
            int start = text.IndexOf(value, StringComparison.Ordinal);
            if (start >= 0)
                output.Add(new TextPaintSpan(new TextRange(start, value.Length), Foreground: foreground));
        }

        private static void AddGeometry(
            ReadOnlySpan<char> text,
            string value,
            IList<GeometryStyleRun> output,
            TextRunStyle style)
        {
            int start = text.IndexOf(value, StringComparison.Ordinal);
            if (start >= 0)
                output.Add(new GeometryStyleRun(start, value.Length, style));
        }
    }
}
