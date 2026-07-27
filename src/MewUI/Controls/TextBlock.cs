namespace Aprillz.MewUI.Controls;

/// <summary>
/// Lightweight text element (WPF-like) that does not carry full <see cref="Control"/> features.
/// Inherits <see cref="TextElement.ForegroundProperty"/>, <see cref="TextElement.FontFamilyProperty"/>,
/// <see cref="TextElement.FontSizeProperty"/>, and <see cref="TextElement.FontWeightProperty"/> so that
/// inherited values propagate naturally from parent controls without style-target interference.
/// </summary>
public partial class TextBlock : TextBlockBase
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<TextBlock>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextChanged());

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    protected override string DisplayText => Text;

    protected virtual void OnTextChanged() => InvalidateTextLayout();
}
