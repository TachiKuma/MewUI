using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement TypographyPage()
    {
        var runDemo = new TextBlock().Width(620).TextWrapping(TextWrapping.Wrap);
        runDemo.Inlines.Add(new Run("Normal text, "));
        runDemo.Inlines.Add(new Run("bold text") { FontWeight = FontWeight.Bold });
        runDemo.Inlines.Add(new Run(", "));
        runDemo.Inlines.Add(new Run("italic text") { Italic = true });
        runDemo.Inlines.Add(new Run(", "));
        runDemo.Inlines.Add(new Run("accent text") { Foreground = Color.FromHex("#D83B01") });
        runDemo.Inlines.Add(new Run(", "));
        runDemo.Inlines.Add(new Run("underlined text") { Decoration = TextDecoration.Underline });
        runDemo.Inlines.Add(new Run(", and "));
        runDemo.Inlines.Add(new Run("struck text") { Decoration = TextDecoration.Strikethrough });
        runDemo.Inlines.Add(new Run(".\nMixed fonts: Segoe UI + "));
        runDemo.Inlines.Add(new Run("Consolas") { FontFamily = "Consolas" });
        runDemo.Inlines.Add(new Run(" + "));
        runDemo.Inlines.Add(new Run("22 pt") { FontSize = 22 });
        runDemo.Inlines.Add(new Run("."));

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
}
