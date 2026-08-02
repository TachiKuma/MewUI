using Aprillz.MewUI;

namespace ICSharpCode.AvalonEdit.Highlighting;

public sealed class HighlightingManager
{
    private readonly Dictionary<string, IHighlightingDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IHighlightingDefinition> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    private HighlightingManager()
    {
        RegisterBuiltIns();
    }

    public static HighlightingManager Instance { get; } = new();
    public IReadOnlyCollection<IHighlightingDefinition> HighlightingDefinitions => _byName.Values;

    public IHighlightingDefinition? GetDefinition(string name)
        => string.IsNullOrWhiteSpace(name) ? null : _byName.GetValueOrDefault(name);

    public IHighlightingDefinition? GetDefinitionByExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        if (!extension.StartsWith('.')) extension = "." + extension;
        return _byExtension.GetValueOrDefault(extension);
    }

    public void RegisterHighlighting(string name, IEnumerable<string> extensions, IHighlightingDefinition highlighting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(highlighting);
        _byName[name] = highlighting;
        foreach (string extension in extensions)
        {
            string normalized = extension.StartsWith('.') ? extension : "." + extension;
            _byExtension[normalized] = highlighting;
        }
    }

    private void RegisterBuiltIns()
    {
        var keyword = new HighlightingColor { Foreground = Color.FromRgb(86, 156, 214) };
        var type = new HighlightingColor { Foreground = Color.FromRgb(78, 201, 176) };
        var text = new HighlightingColor { Foreground = Color.FromRgb(214, 157, 133) };
        var comment = new HighlightingColor { Foreground = Color.FromRgb(106, 153, 85) };
        var number = new HighlightingColor { Foreground = Color.FromRgb(181, 206, 168) };
        var csharp = new HighlightingDefinition("C#")
            .AddColor("Keyword", keyword)
            .AddColor("Type", type)
            .AddColor("String", text)
            .AddColor("Comment", comment)
            .AddColor("Number", number)
            .AddRule(@"//.*$", comment)
            .AddRule(@"""(?:\\.|[^""\\])*""", text)
            .AddRule(@"\b(?:class|struct|interface|enum|record|namespace|using|public|private|protected|internal|static|sealed|abstract|partial|void|return|new|if|else|switch|case|for|foreach|while|do|try|catch|finally|throw|async|await|true|false|null)\b", keyword)
            .AddRule(@"\b(?:string|char|bool|byte|short|int|long|float|double|decimal|object|var)\b", type)
            .AddRule(@"\b\d+(?:\.\d+)?\b", number);
        RegisterHighlighting("C#", [".cs", ".csx"], csharp);

        var tag = new HighlightingColor { Foreground = Color.FromRgb(86, 156, 214) };
        var attribute = new HighlightingColor { Foreground = Color.FromRgb(156, 220, 254) };
        var xml = new HighlightingDefinition("XML")
            .AddRule(@"<!--[\s\S]*?-->", comment)
            .AddRule(@"</?[A-Za-z_][\w:.-]*", tag)
            .AddRule(@"\b[A-Za-z_][\w:.-]*(?=\s*=)", attribute)
            .AddRule(@"""(?:\\.|[^""\\])*""", text);
        RegisterHighlighting("XML", [".xml", ".xaml", ".xshd"], xml);

        var json = new HighlightingDefinition("JSON")
            .AddRule(@"""(?:\\.|[^""\\])*""(?=\s*:)", attribute)
            .AddRule(@"""(?:\\.|[^""\\])*""", text)
            .AddRule(@"\b(?:true|false|null)\b", keyword)
            .AddRule(@"-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", number);
        RegisterHighlighting("JSON", [".json"], json);
    }
}
