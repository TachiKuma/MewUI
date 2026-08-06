using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.MewvalonEdit;

public class TextEditorOptions : INotifyPropertyChanged
{
    private int _indentationSize = 4;
    private bool _convertTabsToSpaces;
    private bool _showSpaces;
    private bool _showTabs;
    private bool _showEndOfLine;
    private bool _showColumnRuler;
    private int _columnRulerPosition = 80;
    private bool _highlightCurrentLine;
    private bool _showBoxForControlCharacters = true;
    private bool _enableHyperlinks = true;
    private bool _enableEmailHyperlinks = true;
    private bool _requireControlModifierForHyperlinkClick = true;
    private bool _cutCopyWholeLine = true;

    public virtual int IndentationSize
    {
        get => _indentationSize;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_indentationSize == value) return;
            Set(ref _indentationSize, value);
            OnPropertyChanged(nameof(IndentationString));
        }
    }

    public virtual bool ConvertTabsToSpaces
    {
        get => _convertTabsToSpaces;
        set
        {
            if (_convertTabsToSpaces == value) return;
            Set(ref _convertTabsToSpaces, value);
            OnPropertyChanged(nameof(IndentationString));
        }
    }

    /// <summary>Text that indents from column 1 to the next indentation level.</summary>
    public string IndentationString => GetIndentationString(1);

    /// <summary>
    /// Text that indents from <paramref name="column"/> to the next indentation level. Converted
    /// tabs fill only up to the next stop, so a tab pressed mid-column does not overshoot it.
    /// </summary>
    public virtual string GetIndentationString(int column)
    {
        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Value must be at least 1.");
        }
        return ConvertTabsToSpaces
            ? new string(' ', IndentationSize - ((column - 1) % IndentationSize))
            : "\t";
    }

    public virtual bool ShowSpaces
    {
        get => _showSpaces;
        set => Set(ref _showSpaces, value);
    }

    /// <summary>Draws a vertical rule at <see cref="ColumnRulerPosition"/>.</summary>
    public virtual bool ShowColumnRuler
    {
        get => _showColumnRuler;
        set => Set(ref _showColumnRuler, value);
    }

    /// <summary>Column the rule sits at, counted in wide spaces.</summary>
    public virtual int ColumnRulerPosition
    {
        get => _columnRulerPosition;
        set => Set(ref _columnRulerPosition, value);
    }

    /// <summary>Paints the line holding the caret in the view's current-line colours.</summary>
    public virtual bool HighlightCurrentLine
    {
        get => _highlightCurrentLine;
        set => Set(ref _highlightCurrentLine, value);
    }

    public virtual bool ShowTabs
    {
        get => _showTabs;
        set => Set(ref _showTabs, value);
    }

    public virtual bool ShowEndOfLine
    {
        get => _showEndOfLine;
        set => Set(ref _showEndOfLine, value);
    }

    /// <summary>
    /// Draws a control character as a box naming it, so an otherwise invisible character shows
    /// without reading as ordinary text. On by default, as in the original.
    /// </summary>
    public virtual bool ShowBoxForControlCharacters
    {
        get => _showBoxForControlCharacters;
        set => Set(ref _showBoxForControlCharacters, value);
    }

    /// <summary>Turns web addresses in the text into links. On by default, as in the original.</summary>
    public virtual bool EnableHyperlinks
    {
        get => _enableHyperlinks;
        set => Set(ref _enableHyperlinks, value);
    }

    /// <summary>Turns mail addresses in the text into links. On by default, as in the original.</summary>
    public virtual bool EnableEmailHyperlinks
    {
        get => _enableEmailHyperlinks;
        set => Set(ref _enableEmailHyperlinks, value);
    }

    /// <summary>Whether a link needs Ctrl held to follow it, rather than a plain click.</summary>
    public virtual bool RequireControlModifierForHyperlinkClick
    {
        get => _requireControlModifierForHyperlinkClick;
        set => Set(ref _requireControlModifierForHyperlinkClick, value);
    }

    /// <summary>Copying with nothing selected takes the whole line. On by default, as in the original.</summary>
    public virtual bool CutCopyWholeLine
    {
        get => _cutCopyWholeLine;
        set => Set(ref _cutCopyWholeLine, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
        => OnPropertyChanged(new PropertyChangedEventArgs(propertyName));

    /// <summary>Raises <see cref="PropertyChanged"/>. Override to see every option change.</summary>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, e);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName!);
    }
}
