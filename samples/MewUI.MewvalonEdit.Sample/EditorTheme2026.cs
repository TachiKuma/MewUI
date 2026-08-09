using System.Reflection;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace Aprillz.MewUI.MewvalonEdit.Sample;

/// <summary>
/// Paints code in the colours of VS Code's 2026 themes. The colours themselves are data
/// (Themes/2026-palette.json, keyed by the TextMate scope each was resolved from); what lives here
/// is which xshd colour name takes which scope.
/// </summary>
/// <remarks>
/// Two limits are inherent to mapping a TextMate theme onto a regex tokenizer. The xshd definitions
/// split keywords far more finely than the theme does, so several xshd names fold onto one scope
/// and lose a distinction the definition drew. And the 2026 themes rely on semantic highlighting
/// for much of what VS Code shows in C#, which no regex tokenizer produces; what this palette gives
/// is the 2026 colours as VS Code would paint them with semantic highlighting off.
/// </remarks>
internal static class EditorTheme2026
{
    private const string RESOURCE = "MewUI.MewvalonEdit.Sample.Themes.2026-palette.json";

    private static readonly Palette _palette = Palette.Load();

    /// <summary>
    /// Installs the palette. A scope left out keeps whatever colour its definition carries, so a
    /// definition this mapping does not cover still draws.
    /// </summary>
    public static void Install()
    {
        var palette = new HighlightingPalette();

        // C#. Which set gets which colour follows the words the set actually holds: Keywords is
        // else/if/switch/for/while, which the theme paints as control flow, not as a plain keyword.
        Map(palette, "keyword.control", "Keywords", "GotoKeywords", "ExceptionKeywords");
        Map(palette, "keyword",
            "NamespaceKeywords", "GetSetAddRemove", "ContextKeywords", "OperatorKeywords",
            "CheckedKeyword", "UnsafeKeywords", "SemanticKeywords");
        Map(palette, "storage.modifier", "Modifiers", "Visibility", "ParameterModifiers");
        Map(palette, "constant.language", "TrueFalse", "NullOrValueKeywords");
        Map(palette, "entity.name.type", "ValueTypeKeywords", "ReferenceTypeKeywords", "TypeKeywords");
        Map(palette, "variable.language", "ThisOrBaseReference");
        Map(palette, "entity.name.function", "MethodCall");
        Map(palette, "meta.preprocessor", "Preprocessor", "PreprocessorSet");

        // Shared across definitions. Every definition names its own scopes, so a colour has to be
        // listed under each name that means the same thing; a name left out draws in the colour its
        // definition carries, which is what makes a language look untouched.
        Map(palette, "comment",
            "Comment", "DocCommentMarker", "CommentMarkerSet", "CommentTags", "JavaDocTags",
            "DocComment", "DocCommentSet", "KnownDocTags", "XmlPunctuation");
        Map(palette, "string", "String", "Char", "Character", "StringInterpolation", "XmlString");
        Map(palette, "constant.numeric", "NumberLiteral", "Number", "Digits", "DateLiteral");
        Map(palette, "keyword.control",
            "ControlFlow", "LoopKeywords", "JumpKeywords", "IterationStatements",
            "SelectionStatements", "JumpStatements", "ControlStatements", "ExceptionHandling",
            "ExceptionHandlingStatements", "CompoundKeywords");
        Map(palette, "storage.modifier",
            "AccessModifiers", "AccessKeywords", "Friend", "FunctionKeywords");
        Map(palette, "entity.name.type", "ValueTypes", "ReferenceTypes", "DataTypes", "OtherTypes", "Void");
        Map(palette, "entity.name.function", "MethodName", "FunctionCall", "Command");
        Map(palette, "constant.language", "Literals", "Constants", "BooleanConstants");
        Map(palette, "meta.preprocessor", "Package", "Namespace");
        Map(palette, "variable.language", "This");
        Map(palette, "variable", "Variable");
        Map(palette, "keyword.operator", "Operators");
        Map(palette, "constant.character.escape", "Escape", "EscapeSequence");

        // XML.
        Map(palette, "entity.name.tag", "XmlTag");
        Map(palette, "entity.other.attribute-name", "AttributeName");
        Map(palette, "string", "AttributeValue");
        Map(palette, "keyword", "Entity");
        Map(palette, "invalid.illegal", "BrokenEntity");
        Map(palette, "punctuation.definition.tag", "CData", "DocType", "XmlDeclaration");

        // HTML and ASPX, which name the same things differently from XML.
        Map(palette, "entity.name.tag", "HtmlTag", "Tags", "ASPSectionStartEndTags");
        Map(palette, "entity.other.attribute-name", "Attributes");
        Map(palette, "invalid.illegal", "UnknownAttribute", "UnknownScriptTag");
        Map(palette, "keyword", "Entities", "EntityReference", "EntityReferenceSet");
        Map(palette, "punctuation.definition.tag", "Assignment", "Slash");
        Map(palette, "keyword.control",
            "ScriptTag", "JavaScriptTag", "VBScriptTag", "JScriptTag", "ASPSection");

        // CSS.
        Map(palette, "entity.other.attribute-name.class.css", "Selector", "Class");
        Map(palette, "support.type.property-name", "Property");
        Map(palette, "string", "Value");
        Map(palette, "punctuation.definition.tag", "Colon", "CurlyBraces");

        // JSON. The definition draws braces and brackets through Object/Array/Expression, which the
        // theme has no rule for, so those keep the definition's colour.
        Map(palette, "support.type.property-name", "FieldName");
        Map(palette, "constant.language", "Bool", "Null");

        // JavaScript, for the HTML and script samples. Built-ins take the support colours the theme
        // keeps apart from user-defined names.
        Map(palette, "keyword.control", "JavaScriptKeyWords");
        Map(palette, "constant.language", "JavaScriptLiterals");
        Map(palette, "support.function", "JavaScriptGlobalFunctions");
        Map(palette, "entity.name.type", "JavaScriptIntrinsics");
        Map(palette, "string.regexp", "Regex");

        // Markdown.
        Map(palette, "markup.heading", "Heading", "Code");
        Map(palette, "markup.bold", "Emphasis", "StrongEmphasis");
        Map(palette, "markup.inserted", "BlockQuote");
        Map(palette, "entity.name.function", "Link", "Image");

        // Patch and diff.
        Map(palette, "markup.inserted", "AddedText");
        Map(palette, "invalid.illegal", "RemovedText");
        Map(palette, "markup.heading", "Header", "FileName");
        Map(palette, "meta.diff.range", "Position");

        HighlightingPalette.Current = palette;
    }

    /// <summary>
    /// Applies the surface colours the theme carries. Only these three are reachable: the selection,
    /// the caret and the current-line highlight come from the MewUI theme and the editor exposes no
    /// way to override them.
    /// </summary>
    public static void ApplyEditorColors(TextEditor editor, bool isDark)
    {
        editor.Background = _palette.Editor("background", isDark);
        editor.Foreground = _palette.Editor("foreground", isDark);
        editor.LineNumbersForeground = _palette.Editor("lineNumber", isDark);
    }

    private static void Map(HighlightingPalette palette, string scope, params string[] names)
    {
        if (!_palette.TryGetToken(scope, out var entry))
        {
            return;
        }
        foreach (string name in names)
        {
            palette.Set(name, entry);
        }
    }

    /// <summary>The colours as loaded, keyed by the scope each was resolved from.</summary>
    private sealed class Palette
    {
        private readonly Dictionary<string, PaletteEntry> _tokens;
        private readonly Dictionary<string, (Color Dark, Color Light)> _editor;

        private Palette(
            Dictionary<string, PaletteEntry> tokens,
            Dictionary<string, (Color Dark, Color Light)> editor)
        {
            _tokens = tokens;
            _editor = editor;
        }

        public static Palette Load()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE)
                ?? throw new InvalidOperationException($"The palette resource '{RESOURCE}' is missing.");
            using var document = JsonDocument.Parse(stream);

            var tokens = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.GetProperty("tokens").EnumerateObject())
            {
                var (dark, light) = ReadPair(property.Value);
                tokens[property.Name] = new PaletteEntry(dark, light);
            }

            var editor = new Dictionary<string, (Color, Color)>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.GetProperty("editor").EnumerateObject())
            {
                editor[property.Name] = ReadPair(property.Value);
            }

            return new Palette(tokens, editor);
        }

        public bool TryGetToken(string scope, out PaletteEntry entry) => _tokens.TryGetValue(scope, out entry);

        public Color Editor(string key, bool isDark)
        {
            var pair = _editor[key];
            return isDark ? pair.Dark : pair.Light;
        }

        private static (Color Dark, Color Light) ReadPair(JsonElement element) => (
            Color.FromHex(element.GetProperty("dark").GetString()),
            Color.FromHex(element.GetProperty("light").GetString()));
    }
}
