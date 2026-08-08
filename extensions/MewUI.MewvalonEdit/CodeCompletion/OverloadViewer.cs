using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

/// <summary>
/// Represents a text between "Up" and "Down" buttons: the index row walks the overloads and the
/// header and content below it show the selected one. The index row hides itself when there is
/// only one overload to show.
/// </summary>
public sealed class OverloadViewer
{
    private readonly StackPanel _indexPanel;
    private readonly TextBlock _indexText;
    private readonly Border _headerHost;
    private readonly Border _contentHost;
    private IOverloadProvider? _provider;

    public OverloadViewer()
    {
        _indexText = new TextBlock { Margin = new Thickness(2, 0, 2, 0) };
        _indexPanel = new StackPanel()
            .Horizontal()
            .Children(
                GlyphButton(GlyphKind.ChevronUp, () => ChangeIndex(-1)),
                _indexText.CenterVertical(),
                GlyphButton(GlyphKind.ChevronDown, () => ChangeIndex(+1)));
        _indexPanel.Margin = new Thickness(0, 0, 4, 0);
        _headerHost = new Border();
        _contentHost = new Border();
        Root = new StackPanel()
            .Vertical()
            .Children(
                new StackPanel().Horizontal().Children(_indexPanel, _headerHost),
                _contentHost);
        Refresh();
    }

    /// <summary>The element an insight window hosts.</summary>
    internal FrameworkElement Root { get; }

    /// <summary>The item provider. The viewer follows its property changes.</summary>
    public IOverloadProvider? Provider
    {
        get => _provider;
        set
        {
            if (ReferenceEquals(_provider, value))
            {
                return;
            }
            if (_provider is IOverloadProvider previous)
            {
                previous.PropertyChanged -= OnProviderPropertyChanged;
            }
            _provider = value;
            if (value is IOverloadProvider current)
            {
                current.PropertyChanged += OnProviderPropertyChanged;
            }
            Refresh();
        }
    }

    /// <summary>Changes the selected index, wrapping around at either end.</summary>
    /// <param name="relativeIndexChange">The relative index change - usual values are +1 or -1.</param>
    public void ChangeIndex(int relativeIndexChange)
    {
        if (_provider is IOverloadProvider provider)
        {
            int newIndex = provider.SelectedIndex + relativeIndexChange;
            if (newIndex < 0)
            {
                newIndex = provider.Count - 1;
            }
            if (newIndex >= provider.Count)
            {
                newIndex = 0;
            }
            provider.SelectedIndex = newIndex;
        }
    }

    private void OnProviderPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => Refresh();

    private void Refresh()
    {
        // The index row means nothing with a single overload, as the original's converter hides it.
        _indexPanel.IsVisible = _provider?.Count > 1;
        _indexText.Text = _provider?.CurrentIndexText ?? string.Empty;
        _headerHost.Child = BuildContent(_provider?.CurrentHeader);
        _contentHost.Child = BuildContent(_provider?.CurrentContent);
    }

    /// <summary>A string renders as wrapped text; an element renders as is; anything else by its text.</summary>
    private static FrameworkElement? BuildContent(object? value) => value switch
    {
        null => null,
        FrameworkElement element => element,
        string text => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        _ => new TextBlock { Text = value.ToString() ?? string.Empty, TextWrapping = TextWrapping.Wrap },
    };

    private static Button GlyphButton(GlyphKind kind, Action onClick)
    {
        var button = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Content = new GlyphElement { Kind = kind },
            Width = 14,
            Height = 14,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        button.StyleName = BuiltInStyles.FlatButton;
        return button.OnClick(onClick);
    }
}
