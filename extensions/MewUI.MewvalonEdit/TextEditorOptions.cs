using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.MewvalonEdit;

public sealed class TextEditorOptions : INotifyPropertyChanged
{
    private int _indentationSize = 4;
    private bool _convertTabsToSpaces;
    private bool _showSpaces;
    private bool _showTabs;
    private bool _showEndOfLine;
    private bool _showColumnRuler;
    private int _columnRulerPosition = 80;
    private bool _highlightCurrentLine;

    public int IndentationSize
    {
        get => _indentationSize;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            Set(ref _indentationSize, value);
        }
    }

    public bool ConvertTabsToSpaces
    {
        get => _convertTabsToSpaces;
        set => Set(ref _convertTabsToSpaces, value);
    }

    public bool ShowSpaces
    {
        get => _showSpaces;
        set => Set(ref _showSpaces, value);
    }

    /// <summary>Draws a vertical rule at <see cref="ColumnRulerPosition"/>.</summary>
    public bool ShowColumnRuler
    {
        get => _showColumnRuler;
        set => Set(ref _showColumnRuler, value);
    }

    /// <summary>Column the rule sits at, counted in wide spaces.</summary>
    public int ColumnRulerPosition
    {
        get => _columnRulerPosition;
        set => Set(ref _columnRulerPosition, value);
    }

    /// <summary>Paints the line holding the caret in the view's current-line colours.</summary>
    public bool HighlightCurrentLine
    {
        get => _highlightCurrentLine;
        set => Set(ref _highlightCurrentLine, value);
    }

    public bool ShowTabs
    {
        get => _showTabs;
        set => Set(ref _showTabs, value);
    }

    public bool ShowEndOfLine
    {
        get => _showEndOfLine;
        set => Set(ref _showEndOfLine, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
