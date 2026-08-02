using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("The initial MewalonEdit sample uses the Windows Direct2D backend.");
}

Win32Platform.Register();
Direct2DBackend.Register();

const string source = """
using System;

namespace MewalonEdit.Sample;

public sealed class GreetingService
{
    public string Create(string name)
    {
        // Highlighting, selection, folding, and editing use the MewUI text engine.
        return $"Hello, {name}!";
    }
}
""";

var editor = new TextEditor
{
    Text = source,
    FontFamily = "Consolas",
    FontSize = 15,
    WordWrap = false,
    ShowLineNumbers = true,
    SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#")
};

var foldingManager = FoldingManager.Install(editor);
int bodyStart = source.IndexOf('{');
int bodyEnd = source.LastIndexOf('}') + 1;
foldingManager.UpdateFoldings(
    [new NewFolding(bodyStart, bodyEnd) { Name = "{ … }" }],
    firstErrorOffset: -1);
var folding = foldingManager.AllFoldings.Single();
var search = SearchPanel.Install(editor.TextArea);
search.SearchPattern = "string";

var toggle = new Button()
    .Content("Toggle class folding")
    .OnClick(() => folding.IsFolded = !folding.IsFolded)
    .DockTop();

var markers = new Button()
    .Content("Toggle whitespace markers")
    .OnClick(() =>
    {
        editor.Options.ShowSpaces = !editor.Options.ShowSpaces;
        editor.Options.ShowTabs = editor.Options.ShowSpaces;
        editor.Options.ShowEndOfLine = editor.Options.ShowSpaces;
    })
    .DockTop();

var findNext = new Button()
    .Content("Find next 'string'")
    .OnClick(() => search.FindNext())
    .DockTop();

var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }
    .Children(toggle, markers, findNext)
    .DockTop();

var window = new Window()
    .Title("MewalonEdit")
    .Resizable(900, 650)
    .Build(w => w.Content(
        new DockPanel()
            .Padding(12)
            .Spacing(8)
            .Children(toolbar, editor)));

DispatcherTimer? smokeTimer = null;
if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
{
    int phase = 0;
    smokeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500));
    smokeTimer.Tick += () =>
    {
        switch (phase++)
        {
            case 0:
                folding.IsFolded = true;
                break;
            case 1:
                folding.IsFolded = false;
                editor.Options.ShowSpaces = true;
                search.FindNext(0);
                editor.Document.Insert(editor.Document.TextLength, "\n// smoke edit");
                break;
            case 2:
                GC.Collect();
                GC.WaitForPendingFinalizers();
                break;
            default:
                smokeTimer.Stop();
                window.Close();
                break;
        }
    };
    window.Loaded += smokeTimer.Start;
}

Application.Run(window);
