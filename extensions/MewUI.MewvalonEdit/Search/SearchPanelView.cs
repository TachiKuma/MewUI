using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

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
            Margin = new Thickness(0, 4, 16, 0),
            Padding = new Thickness(6),
            BorderThickness = 1
        };
        _patternBox = new TextBox().Width(160).Placeholder("Find");
        _patternBox.TextChanged += value =>
        {
            _panel.SearchPattern = value;
            UpdateStatus();
        };
        _status = new TextBlock();

        var matchCase = new CheckBox { IsChecked = panel.MatchCase }
            .Content(panel.Localization.MatchCaseText)
            .OnCheckedChanged(value => { _panel.MatchCase = value == true; UpdateStatus(); });
        var wholeWords = new CheckBox { IsChecked = panel.WholeWords }
            .Content(panel.Localization.MatchWholeWordsText)
            .OnCheckedChanged(value => { _panel.WholeWords = value == true; UpdateStatus(); });
        var useRegex = new CheckBox { IsChecked = panel.UseRegex }
            .Content(panel.Localization.UseRegexText)
            .OnCheckedChanged(value => { _panel.UseRegex = value == true; UpdateStatus(); });

        Root.Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 }.Children(
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }.Children(
                _patternBox,
                new Button().Content("<").OnClick(() => { _panel.FindPrevious(); UpdateStatus(); }),
                new Button().Content(">").OnClick(() => { _panel.FindNext(); UpdateStatus(); }),
                new Button().Content("X").OnClick(_panel.Close)),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }.Children(
                matchCase, wholeWords, useRegex),
            _status);
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
