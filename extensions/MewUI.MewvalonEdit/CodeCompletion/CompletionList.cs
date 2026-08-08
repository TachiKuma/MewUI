using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

/// <summary>
/// The list used inside the <see cref="CompletionWindow"/>. It filters and selects among the
/// <see cref="CompletionData"/> as the user types, and raises <see cref="InsertionRequested"/>
/// when an entry is chosen.
/// </summary>
public sealed class CompletionList
{
    private readonly ListBox _listBox;
    private readonly List<ICompletionData> _completionData = [];
    private List<ICompletionData> _visibleItems;
    // SelectItem gets called for every caret move; this executes the work only when the query changed.
    private string? _currentText;
    private List<ICompletionData>? _currentList;
    private double _itemHeight = 18;

    public CompletionList()
    {
        _listBox = new ListBox();
        _listBox.WithTheme((theme, listBox) =>
        {
            _itemHeight = Math.Max(18, theme.Metrics.BaseControlHeight - 2);
            listBox.ItemHeight = _itemHeight;
        });
        _listBox.SelectionChanged += _ => SelectionChanged?.Invoke(this, EventArgs.Empty);
        // Only double clicks on the items commit; the scroll bar is part of the same control here,
        // but a double click on it never changes the selection, so committing the selected item is
        // still what the user meant.
        // MewUI event args do not derive from EventArgs, so the trigger cannot ride along as the
        // original's insertionRequestEventArgs does.
        _listBox.MouseDoubleClick += e =>
        {
            if (e.Button == MouseButton.Left && _listBox.SelectedIndex >= 0)
            {
                e.Handled = true;
                RequestInsertion(EventArgs.Empty);
            }
        };
        _visibleItems = _completionData;
    }

    /// <summary>The element the completion window hosts.</summary>
    internal FrameworkElement Root => _listBox;

    /// <summary>
    /// If true, the list is filtered to show only matching items, and substrings match. If false,
    /// the old behavior: no filtering, search by StartsWith.
    /// </summary>
    public bool IsFiltering { get; set; } = true;

    /// <summary>The list completion data can be added to.</summary>
    public IList<ICompletionData> CompletionData => _completionData;

    /// <summary>The items currently shown, after filtering.</summary>
    public IReadOnlyList<ICompletionData> VisibleItems => _visibleItems;

    /// <summary>
    /// Raised when the list indicates that the user has chosen an entry to be completed.
    /// </summary>
    public event EventHandler<EventArgs>? InsertionRequested;

    /// <summary>Occurs when <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    public void RequestInsertion(EventArgs e) => InsertionRequested?.Invoke(this, e);

    public ICompletionData? SelectedItem
    {
        get
        {
            int index = _listBox.SelectedIndex;
            return index >= 0 && index < _visibleItems.Count ? _visibleItems[index] : null;
        }
        set
        {
            int index = value is null ? -1 : _visibleItems.IndexOf(value);
            _listBox.SelectedIndex = index;
        }
    }

    /// <summary>Scrolls the item into view.</summary>
    public void ScrollIntoView(ICompletionData item)
    {
        int index = _visibleItems.IndexOf(item);
        if (index >= 0)
        {
            _listBox.ScrollIntoView(index);
        }
    }

    private int VisibleItemCount
    {
        get
        {
            double height = _listBox.Bounds.Height;
            if (height <= 0 || _itemHeight <= 0)
            {
                return 10;
            }
            return Math.Max(3, (int)(height / _itemHeight));
        }
    }

    /// <summary>
    /// Handles a key while the focus stays on the text editor: the movement keys walk the list,
    /// Tab and Enter commit, everything else is left for the editor to type.
    /// </summary>
    public void HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                e.Handled = true;
                SelectIndex(_listBox.SelectedIndex + 1);
                break;
            case Key.Up:
                e.Handled = true;
                SelectIndex(_listBox.SelectedIndex - 1);
                break;
            case Key.PageDown:
                e.Handled = true;
                SelectIndex(_listBox.SelectedIndex + VisibleItemCount);
                break;
            case Key.PageUp:
                e.Handled = true;
                SelectIndex(_listBox.SelectedIndex - VisibleItemCount);
                break;
            case Key.Home:
                e.Handled = true;
                SelectIndex(0);
                break;
            case Key.End:
                e.Handled = true;
                SelectIndex(_visibleItems.Count - 1);
                break;
            case Key.Tab:
            case Key.Enter:
                e.Handled = true;
                RequestInsertion(EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Selects the best match for the query, filtering the items when <see cref="IsFiltering"/>.
    /// </summary>
    public void SelectItem(string text)
    {
        if (text == _currentText)
        {
            return;
        }
        if (IsFiltering)
        {
            SelectItemFiltering(text);
        }
        else
        {
            SelectItemWithStart(text);
        }
        _currentText = text;
    }

    private void SelectItemFiltering(string query)
    {
        // When the user typed one more character, only what is already displayed needs refiltering.
        var listToFilter = _currentList is not null
            && !string.IsNullOrEmpty(_currentText) && !string.IsNullOrEmpty(query)
            && query.StartsWith(_currentText, StringComparison.Ordinal)
                ? _currentList
                : _completionData;

        // The currently selected item is preferred over every priority, e.g.
        // "DateTimeKind k = (*cc here suggests DateTimeKind*)".
        var suggestedItem = SelectedItem;

        var matched = new List<ICompletionData>();
        int bestIndex = -1;
        int bestQuality = -1;
        double bestPriority = 0;
        foreach (var item in listToFilter)
        {
            int quality = GetMatchQuality(item.Text, query);
            if (quality <= 0)
            {
                continue;
            }
            double priority = ReferenceEquals(item, suggestedItem) ? double.PositiveInfinity : item.Priority;
            if (quality > bestQuality || (quality == bestQuality && priority > bestPriority))
            {
                bestIndex = matched.Count;
                bestPriority = priority;
                bestQuality = quality;
            }
            matched.Add(item);
        }
        _currentList = matched;
        SetVisibleItems(matched);
        SelectIndexCentered(bestIndex);
    }

    private void SelectItemWithStart(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return;
        }
        int suggestedIndex = _listBox.SelectedIndex;
        int bestIndex = -1;
        int bestQuality = -1;
        double bestPriority = 0;
        for (int index = 0; index < _completionData.Count; index++)
        {
            int quality = GetMatchQuality(_completionData[index].Text, query);
            if (quality < 0)
            {
                continue;
            }
            double priority = _completionData[index].Priority;
            bool useThisItem;
            if (bestQuality < quality)
            {
                useThisItem = true;
            }
            else if (bestIndex == suggestedIndex)
            {
                useThisItem = false;
            }
            else if (index == suggestedIndex)
            {
                // Prefer the suggested item, regardless of its priority.
                useThisItem = bestQuality == quality;
            }
            else
            {
                useThisItem = bestQuality == quality && bestPriority < priority;
            }
            if (useThisItem)
            {
                bestIndex = index;
                bestPriority = priority;
                bestQuality = quality;
            }
        }
        SelectIndexCentered(bestIndex);
    }

    private void SelectIndexCentered(int bestIndex)
    {
        if (bestIndex < 0)
        {
            _listBox.SelectedIndex = -1;
        }
        else
        {
            SelectIndex(bestIndex);
        }
    }

    private void SelectIndex(int index)
    {
        if (_visibleItems.Count == 0)
        {
            return;
        }
        _listBox.SelectedIndex = Math.Clamp(index, 0, _visibleItems.Count - 1);
    }

    /// <summary>Shows every item again after filtering narrowed the list.</summary>
    internal void ResetVisibleItems()
    {
        _currentText = null;
        _currentList = null;
        SetVisibleItems(_completionData);
    }

    private void SetVisibleItems(List<ICompletionData> items)
    {
        _visibleItems = items;
        _listBox.ItemsSource = ItemsView.Create<ICompletionData>(
            items, static item => item.Content as string ?? item.Text);
    }

    private int GetMatchQuality(string itemText, string query)
    {
        if (itemText is null)
        {
            throw new ArgumentNullException(nameof(itemText), "ICompletionData.Text returned null");
        }

        // Qualities:
        //   8 = full match case sensitive
        //   7 = full match
        //   6 = match start case sensitive
        //   5 = match start
        //   4 = match CamelCase when the query is 1 or 2 characters
        //   3 = match substring case sensitive
        //   2 = match substring
        //   1 = match CamelCase
        //  -1 = no match
        if (query == itemText)
        {
            return 8;
        }
        if (string.Equals(itemText, query, StringComparison.InvariantCultureIgnoreCase))
        {
            return 7;
        }
        if (itemText.StartsWith(query, StringComparison.InvariantCulture))
        {
            return 6;
        }
        if (itemText.StartsWith(query, StringComparison.InvariantCultureIgnoreCase))
        {
            return 5;
        }

        bool? camelCaseMatch = null;
        if (query.Length <= 2)
        {
            camelCaseMatch = CamelCaseMatch(itemText, query);
            if (camelCaseMatch == true)
            {
                return 4;
            }
        }

        // Substring matches exist only in the filtering (new) behavior.
        if (IsFiltering)
        {
            if (itemText.Contains(query, StringComparison.InvariantCulture))
            {
                return 3;
            }
            if (itemText.Contains(query, StringComparison.InvariantCultureIgnoreCase))
            {
                return 2;
            }
        }

        camelCaseMatch ??= CamelCaseMatch(itemText, query);
        return camelCaseMatch == true ? 1 : -1;
    }

    private static bool CamelCaseMatch(string text, string query)
    {
        // The first letter of the text counts regardless of its case, so camelCase text matches
        // as well as PascalCase text ("cct" matches "camelCaseText").
        var firstLetterOfEachWord = text.Take(1).Concat(text.Skip(1).Where(char.IsUpper));
        int queryIndex = 0;
        foreach (char letter in firstLetterOfEachWord)
        {
            if (queryIndex > query.Length - 1)
            {
                // A partial CamelCase match is a match: "CQ" matches "CodeQualityAnalysis".
                return true;
            }
            if (char.ToUpperInvariant(query[queryIndex]) != char.ToUpperInvariant(letter))
            {
                return false;
            }
            queryIndex++;
        }
        return queryIndex >= query.Length;
    }
}
