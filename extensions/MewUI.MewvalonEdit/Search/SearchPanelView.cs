using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Search;

/// <summary>
/// The panel the reader types into. AvalonEdit puts the same row of controls in an adorner over the
/// text area; here it is a child of the editor's overlay layer, which is what that layer is for.
/// </summary>
internal sealed class SearchPanelView
{
    private readonly SearchPanel _panel;
    private readonly TextBox _patternBox;
    private readonly TextBlock _status;

    /// <summary>The element put on the editor's overlay layer.</summary>
    public Border Root { get; }

    public SearchPanelView(SearchPanel panel)
    {
        _panel = panel;
        Root = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Padding = new Thickness(8),
            BorderThickness = 1
        };
        Root.WithTheme(static (theme, root) =>
        {
            root.CornerRadius = theme.Metrics.ControlCornerRadius;
            root.Background = theme.Palette.ContainerBackground;
            root.BorderBrush = theme.Palette.ControlBorder;
            // The panel hangs inside the editor and the font properties inherit, so without these
            // the controls come out in the document's monospace font at the document's size.
            root.FontFamily(theme.Metrics.FontFamily).FontSize(theme.Metrics.FontSize);
        });
        _patternBox = new TextBox().Width(160).Placeholder("Find");
        _patternBox.TextChanged += value =>
        {
            _panel.SearchPattern = value;
            UpdateStatus();
        };
        // Keys bubble from the focused control through this panel, never through the editing
        // surface, so the search keys have to be answered here as well or typing in the box
        // strands them: F3 and Enter walk the matches, Escape puts the keyboard back in the text.
        Root.KeyDown += e =>
        {
            if (e.Handled)
            {
                return;
            }
            switch (e.Key)
            {
                case Key.Enter or Key.F3 when (e.Modifiers & ModifierKeys.Shift) != 0:
                    _panel.FindPrevious();
                    UpdateStatus();
                    e.Handled = true;
                    break;
                case Key.Enter or Key.F3:
                    _panel.FindNext();
                    UpdateStatus();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    _panel.Close();
                    e.Handled = true;
                    break;
            }
        };

        new TextBlock()
            .Ref(out _status)
            .Bind(TextBlock.IsVisibleProperty, _status, TextBlock.TextProperty, x => x.Length > 0);

        var matchCase = OptionToggle(
            new TextBlock().Text("Aa"),
            panel.Localization.MatchCaseText,
            panel.MatchCase,
            value => _panel.MatchCase = value);
        var wholeWords = OptionToggle(
            new TextBlock()
                .WithTheme((t, c) => c.FontSize(t.Metrics.FontSizeSmall))
                .Inlines(new Run().Text("abc").Decoration(TextDecoration.Underline)),
            panel.Localization.MatchWholeWordsText,
            panel.WholeWords,
            value => _panel.WholeWords = value);
        var useRegex = OptionToggle(
            new TextBlock().Text(".*"),
            panel.Localization.UseRegexText,
            panel.UseRegex,
            value => _panel.UseRegex = value);

        Root.Child = new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        _patternBox,
                        GlyphButton(GlyphKind.ChevronUp, () => { _panel.FindPrevious(); UpdateStatus(); }),
                        GlyphButton(GlyphKind.ChevronDown, () => { _panel.FindNext(); UpdateStatus(); }),
                        GlyphButton(GlyphKind.Cross, _panel.Close)),
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        matchCase, wholeWords, useRegex),
                _status);
    }

    /// <summary>A search option as a compact toggle, its meaning carried by the tooltip.</summary>
    private ToggleButton OptionToggle(TextBlock glyph, string tooltip, bool initial, Action<bool> apply)
    {
        var toggle = new ToggleButton
        {
            Content = glyph.Center(),
            Padding = new Thickness(0),
            IsChecked = initial
        };
        return toggle
            .WithTheme((t, c) => c.Width(t.Metrics.BaseControlHeight))
            .ToolTip(tooltip)
            .OnCheckedChanged(value => { apply(value); UpdateStatus(); });
    }

    /// <summary>The walk and close buttons: a bare 20x20 square around a stroke glyph.</summary>
    private static Button GlyphButton(GlyphKind kind, Action onClick)
    {
        var button = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Content = new GlyphElement { Kind = kind },
            Width = 20,
            Height = 20,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        button.StyleName = BuiltInStyles.FlatButton;
        return button.OnClick(onClick);
    }

    /// <summary>Puts the caret in the search box and selects what is there, as reopening should.</summary>
    public void Reactivate()
    {
        _patternBox.Text = _panel.SearchPattern;
        _patternBox.Focus();
        _patternBox.SelectAll();
    }

    public void UpdateStatus()
        => _status.Text = _panel.Results.Count == 0 && _panel.SearchPattern.Length > 0
            ? _panel.Localization.NoMatchesFoundText
            : string.Empty;
}
