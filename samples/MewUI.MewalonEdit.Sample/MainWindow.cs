using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;

namespace MewvalonEdit.Sample;

public sealed class MainWindow : Window
{
    private readonly TextEditor _editor;
    private readonly SearchPanel _search;
    private readonly FoldingManager _foldingManager;
    private readonly BraceFoldingStrategy _braceFolding = new();
    private readonly TextBlock _position = new();
    private readonly TextBlock _selection = new();
    private readonly TextBlock _documentState = new();
    private readonly TextBlock _searchState = new();
    private readonly TextBlock _encoding = new() { Text = "UTF-8" };
    private readonly Border _optionsPanel;
    private DispatcherTimer? _smokeTimer;

    public MainWindow()
    {
        Title = "MewalonEdit Sample";
        WindowSize = WindowSize.Resizable(1200, 800);

        _editor = new TextEditor
        {
            FontFamily = "Consolas",
            FontSize = 14,
            ShowLineNumbers = true,
            WordWrap = false
        };
        _search = SearchPanel.Install(_editor.TextArea);
        _foldingManager = FoldingManager.Install(_editor.TextArea);
        _optionsPanel = CreateOptionsPanel();
        _optionsPanel.IsVisible = false;

        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        _editor.TextArea.SelectionChanged += (_, _) => UpdateStatus();
        _editor.TextChanged += (_, _) =>
        {
            UpdateDocumentState();
            if (_editor.SyntaxHighlighting?.Name == "C#") UpdateFoldings(braces: true);
        };

        LoadSample(SampleText.CSharp, "C#");
        Content = new DockPanel()
            .Children(
                CreateToolbar().DockTop(),
                CreateStatusBar().DockBottom(),
                _optionsPanel.DockRight(),
                _editor);
    }

    public void EnableSmokeTest()
    {
        int phase = 0;
        _smokeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(350));
        _smokeTimer.Tick += () =>
        {
            switch (phase++)
            {
                case 0:
                    LoadSample(SampleText.Xml, "XML");
                    break;
                case 1:
                    LoadSample(SampleText.CSharp, "C#");
                    ToggleFirstFolding();
                    break;
                case 2:
                    ToggleFirstFolding();
                    _editor.Options.ShowSpaces = true;
                    _search.SearchPattern = "Result";
                    _search.FindNext(0);
                    break;
                case 3:
                    _editor.Document.Insert(_editor.Document.TextLength, "\n// smoke edit");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    break;
                default:
                    _smokeTimer.Stop();
                    Close();
                    break;
            }
        };
        Loaded += _smokeTimer.Start;
    }

    private FrameworkElement CreateToolbar()
    {
        var searchBox = new TextBox().Width(150).Placeholder("Find text...");
        searchBox.TextChanged += value =>
        {
            _search.SearchPattern = value;
            _searchState.Text = $"Matches: {_search.Results.Count}";
        };

        return new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Thickness(8)
        }.Children(
            new Button().Content("C#").OnClick(() => LoadSample(SampleText.CSharp, "C#")),
            new Button().Content("XML").OnClick(() => LoadSample(SampleText.Xml, "XML")),
            new Button().Content("JSON").OnClick(() => LoadSample(SampleText.Json, "JSON")),
            new Button().Content("Long").OnClick(() => LoadSample(SampleText.LongDocument(), "C#")),
            new Button().Content("Undo").OnClick(_editor.Undo),
            new Button().Content("Redo").OnClick(_editor.Redo),
            searchBox,
            new Button().Content("Find next").OnClick(() => _search.FindNext()),
            new Button().Content("Replace all").OnClick(() =>
            {
                int count = _search.ReplaceAll("match");
                _searchState.Text = $"Replaced: {count}";
            }),
            new Button().Content("Toggle fold").OnClick(ToggleFirstFolding),
            new Button().Content("Complete").OnClick(CompleteCurrentWord),
            new Button().Content("Insert template").OnClick(InsertCodeTemplate),
            new Button().Content("Options").OnClick(() => _optionsPanel.IsVisible = !_optionsPanel.IsVisible),
            new Button().Content("Theme").OnClick(ToggleTheme));
    }

    private Border CreateOptionsPanel()
    {
        CheckBox Toggle(string title, bool initial, Action<bool> apply)
            => new CheckBox { IsChecked = initial }
                .Content(title)
                .OnCheckedChanged(value => apply(value == true));

        var indentationSize = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 16,
            Step = 1,
            Format = "0",
            Value = _editor.Options.IndentationSize,
            Width = 80
        };
        indentationSize.ValueChanged += value => _editor.Options.IndentationSize = (int)value;

        return new Border
        {
            Width = 340,
            Padding = new Thickness(12),
            BorderThickness = 1,
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8
            }.Children(
                new TextBlock().Text("Editor options").FontSize(16).Bold(),
                Toggle("Word wrap", _editor.WordWrap, value => _editor.WordWrap = value),
                Toggle("Show line numbers", _editor.ShowLineNumbers, value => _editor.ShowLineNumbers = value),
                Toggle("Show spaces", _editor.Options.ShowSpaces, value => _editor.Options.ShowSpaces = value),
                Toggle("Show tabs", _editor.Options.ShowTabs, value => _editor.Options.ShowTabs = value),
                Toggle("Show end-of-line", _editor.Options.ShowEndOfLine, value => _editor.Options.ShowEndOfLine = value),
                Toggle("Convert tabs to spaces", _editor.Options.ConvertTabsToSpaces,
                    value => _editor.Options.ConvertTabsToSpaces = value),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }
                    .Children(new TextBlock().Text("Indentation size").Width(180), indentationSize),
                Toggle("Read only", _editor.IsReadOnly, value => _editor.IsReadOnly = value))
        }.WithTheme((theme, border) => border
            .Background(theme.Palette.ContainerBackground)
            .BorderBrush(theme.Palette.ControlBorder));
    }

    private FrameworkElement CreateStatusBar()
        => new Border()
            .Padding(new Thickness(8, 4, 8, 4))
            .WithTheme((theme, border) => border.Background(theme.Palette.ContainerBackground))
            .Child(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 18
            }.Children(_position, _selection, _documentState, _encoding, _searchState));

    private void LoadSample(string text, string highlightingName)
    {
        _editor.Text = text;
        _editor.CaretOffset = 0;
        _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(highlightingName);
        _search.Refresh();
        UpdateFoldings(highlightingName == "C#");
        UpdateStatus();
        UpdateDocumentState(highlightingName);
    }

    private void UpdateFoldings(bool braces)
    {
        if (braces)
            _braceFolding.UpdateFoldings(_foldingManager, _editor.Document);
        else
            _foldingManager.UpdateFoldings([], -1);
    }

    private void ToggleFirstFolding()
    {
        var folding = _foldingManager.AllFoldings.FirstOrDefault();
        if (folding is not null) folding.IsFolded = !folding.IsFolded;
    }

    private void CompleteCurrentWord()
    {
        int end = _editor.CaretOffset;
        int start = end;
        while (start > 0 && char.IsLetterOrDigit(_editor.Document.GetCharAt(start - 1))) start--;
        var session = new CompletionSession(_editor, start);
        session.SetItems(SampleCompletionData.All);
        session.Complete();
    }

    private void InsertCodeTemplate()
    {
        const string template = "for (int index = 0; index < count; index++)\n{\n    \n}";
        int start = _editor.SelectionStart;
        _editor.TextArea.ReplaceSelection(template);
        _editor.CaretOffset = Math.Min(_editor.Document.TextLength, start + template.IndexOf("    ", StringComparison.Ordinal) + 4);
    }

    private static void ToggleTheme()
    {
        var application = Application.Current;
        application.SetThemeMode(application.Theme.IsDark ? ThemeVariant.Light : ThemeVariant.Dark);
    }

    private void UpdateStatus()
    {
        var caret = _editor.TextArea.Caret;
        _position.Text = $"Ln {caret.Line}, Col {caret.Column} (Offset {caret.Offset})";
        _selection.Text = $"Selection: {_editor.SelectionLength}";
    }

    private void UpdateDocumentState(string? highlightingName = null)
    {
        string language = highlightingName ?? _editor.SyntaxHighlighting?.Name ?? "Plain text";
        _documentState.Text = $"{language} · {_editor.LineCount:N0} lines · {_editor.Document.TextLength:N0} chars";
        _searchState.Text = $"Matches: {_search.Results.Count}";
    }
}
