using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.CodeCompletion;

public sealed class CompletionSession
{
    private readonly TextEditor _editor;
    private readonly List<ICompletionData> _allItems = [];
    private readonly List<ICompletionData> _filteredItems = [];
    private string _filterText = string.Empty;

    public CompletionSession(TextEditor editor, int startOffset)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if ((uint)startOffset > (uint)editor.Document.TextLength)
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        StartOffset = startOffset;
    }

    public int StartOffset { get; }
    public int EndOffset => _editor.CaretOffset;
    public IReadOnlyList<ICompletionData> Items => _allItems;
    public IReadOnlyList<ICompletionData> FilteredItems => _filteredItems;
    public ICompletionData? SelectedItem { get; private set; }
    public string FilterText => _filterText;

    public void SetItems(IEnumerable<ICompletionData> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _allItems.Clear();
        _allItems.AddRange(items.Where(item => item is not null));
        Refresh();
    }

    public void Refresh()
    {
        int end = Math.Clamp(_editor.CaretOffset, StartOffset, _editor.Document.TextLength);
        _filterText = _editor.Document.GetText(StartOffset, end - StartOffset);
        _filteredItems.Clear();
        _filteredItems.AddRange(_allItems
            .Where(item => item.Text.StartsWith(_filterText, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Text, StringComparer.CurrentCultureIgnoreCase));
        SelectedItem = _filteredItems.FirstOrDefault();
    }

    public bool SelectNext(int delta)
    {
        if (_filteredItems.Count == 0) return false;
        int index = SelectedItem is null ? 0 : _filteredItems.IndexOf(SelectedItem);
        index = (index + delta) % _filteredItems.Count;
        if (index < 0) index += _filteredItems.Count;
        SelectedItem = _filteredItems[index];
        return true;
    }

    public bool Complete(EventArgs? insertionRequestEventArgs = null)
    {
        if (SelectedItem is null) return false;
        int end = Math.Clamp(_editor.CaretOffset, StartOffset, _editor.Document.TextLength);
        SelectedItem.Complete(_editor.TextArea, new SimpleSegment(StartOffset, end - StartOffset), insertionRequestEventArgs ?? EventArgs.Empty);
        return true;
    }
}
