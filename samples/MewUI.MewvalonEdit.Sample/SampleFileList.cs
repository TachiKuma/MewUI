using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewvalonEdit.Sample;

/// <summary>
/// The reference documents deployed beside the sample, listed for opening. They come from the
/// original's sample project and are read from disk rather than embedded, so the sample opens a
/// file the way a user does.
/// </summary>
public sealed class SampleFileList : UserControl
{
    private const string FOLDER = "samples";

    private readonly ListBox _list = new();

    public SampleFileList()
    {
        Width = 190;
        Padding = new Thickness(8);

        var files = EnumerateFiles().Select(static path => new SampleFile(path)).ToList();
        _list.Items(files, static file => file.Name, keySelector: static file => file.Path);
        _list.SelectionChanged += item =>
        {
            if (item is SampleFile file)
            {
                Opened?.Invoke(file);
            }
        };

        Content = new DockPanel()
            .Spacing(6)
            .Children(
                new TextBlock { Text = "Files", FontWeight = FontWeight.Bold }.DockTop(),
                _list);
    }

    /// <summary>Raised when a file in the list is picked.</summary>
    public event Action<SampleFile>? Opened;

    /// <summary>Files deployed next to the executable, sorted by name.</summary>
    private static IEnumerable<string> EnumerateFiles()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, FOLDER);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            : [];
    }
}

/// <summary>One deployed document. Reads its text on demand, since the list only needs the name.</summary>
public sealed class SampleFile(string path)
{
    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(path);

    /// <summary>Highlighting the extension asks for, or null when there is none for it.</summary>
    public string? HighlightingName => System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "HTML",
        ".xml" or ".xshd" => "XML",
        ".cs" => "C#",
        ".json" => "JSON",
        _ => null
    };

    public string ReadText() => File.ReadAllText(Path);

    public override string ToString() => Name;
}
