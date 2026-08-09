using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace Aprillz.MewUI.MewvalonEdit.Sample;

/// <summary>
/// The colours VS Code's 2026 themes paint code in, mapped onto the scope names the bundled xshd
/// definitions use. Values come from flattening each theme's include chain
/// (2026-dark -> dark_modern -> dark_plus -> dark_vs, and the light equivalent) and resolving the
/// TextMate scope in the second column against it.
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
    // Token colours, resolved once from the flattened themes.
    private const string COMMENT_DARK = "#8b949e", COMMENT_LIGHT = "#6e7781";
    private const string STRING_DARK = "#a5d6ff", STRING_LIGHT = "#0a3069";
    private const string NUMBER_DARK = "#b5cea8", NUMBER_LIGHT = "#098658";
    private const string CONSTANT_DARK = "#569cd6", CONSTANT_LIGHT = "#0000ff";
    private const string KEYWORD_DARK = "#ff7b72", KEYWORD_LIGHT = "#cf222e";
    private const string CONTROL_DARK = "#C586C0", CONTROL_LIGHT = "#AF00DB";
    private const string MODIFIER_DARK = "#569cd6", MODIFIER_LIGHT = "#0000ff";
    private const string LANGUAGE_VARIABLE_DARK = "#79c0ff", LANGUAGE_VARIABLE_LIGHT = "#0550ae";
    private const string FUNCTION_DARK = "#d2a8ff", FUNCTION_LIGHT = "#8250df";
    private const string TYPE_DARK = "#4EC9B0", TYPE_LIGHT = "#267f99";
    private const string PREPROCESSOR_DARK = "#569cd6", PREPROCESSOR_LIGHT = "#0000ff";
    private const string TAG_DARK = "#7ee787", TAG_LIGHT = "#116329";
    private const string ATTRIBUTE_DARK = "#9cdcfe", ATTRIBUTE_LIGHT = "#e50000";
    private const string PROPERTY_DARK = "#9cdcfe", PROPERTY_LIGHT = "#e50000";
    private const string TAG_PUNCTUATION_DARK = "#808080", TAG_PUNCTUATION_LIGHT = "#800000";
    private const string REGEX_DARK = "#a5d6ff", REGEX_LIGHT = "#0a3069";
    private const string SUPPORT_FUNCTION_DARK = "#DCDCAA", SUPPORT_FUNCTION_LIGHT = "#795E26";
    private const string INVALID_DARK = "#ffa198", INVALID_LIGHT = "#82071e";
    private const string ESCAPE_DARK = "#d7ba7d", ESCAPE_LIGHT = "#EE0000";
    private const string VARIABLE_DARK = "#ffa657", VARIABLE_LIGHT = "#953800";
    private const string OPERATOR_DARK = "#d4d4d4", OPERATOR_LIGHT = "#000000";
    private const string SELECTOR_DARK = "#d7ba7d", SELECTOR_LIGHT = "#800000";
    private const string HEADING_DARK = "#79c0ff", HEADING_LIGHT = "#0550ae";
    private const string EMPHASIS_DARK = "#c9d1d9", EMPHASIS_LIGHT = "#1f2328";
    private const string INSERTED_DARK = "#7ee787", INSERTED_LIGHT = "#116329";
    private const string DIFF_RANGE_DARK = "#d2a8ff", DIFF_RANGE_LIGHT = "#8250df";

    // Editor surface colours from each theme's colors section.
    private const string BACKGROUND_DARK = "#121314", BACKGROUND_LIGHT = "#FFFFFF";
    private const string FOREGROUND_DARK = "#BBBEBF", FOREGROUND_LIGHT = "#202020";
    private const string LINE_NUMBER_DARK = "#858889", LINE_NUMBER_LIGHT = "#606060";

    /// <summary>
    /// Installs the palette. A scope left out keeps whatever colour its definition carries, so a
    /// definition this mapping does not cover still draws.
    /// </summary>
    public static void Install()
    {
        var palette = new HighlightingPalette();

        // C#. Which set gets which colour follows the words the set actually holds: Keywords is
        // else/if/switch/for/while, which the theme paints as control flow, not as a plain keyword.
        Map(palette, CONTROL_DARK, CONTROL_LIGHT, "Keywords", "GotoKeywords", "ExceptionKeywords");
        Map(palette, KEYWORD_DARK, KEYWORD_LIGHT,
            "NamespaceKeywords", "GetSetAddRemove", "ContextKeywords", "OperatorKeywords",
            "CheckedKeyword", "UnsafeKeywords", "SemanticKeywords");
        Map(palette, MODIFIER_DARK, MODIFIER_LIGHT, "Modifiers", "Visibility", "ParameterModifiers");
        Map(palette, CONSTANT_DARK, CONSTANT_LIGHT, "TrueFalse", "NullOrValueKeywords");
        Map(palette, TYPE_DARK, TYPE_LIGHT, "ValueTypeKeywords", "ReferenceTypeKeywords", "TypeKeywords");
        Map(palette, LANGUAGE_VARIABLE_DARK, LANGUAGE_VARIABLE_LIGHT, "ThisOrBaseReference");
        Map(palette, FUNCTION_DARK, FUNCTION_LIGHT, "MethodCall");
        Map(palette, PREPROCESSOR_DARK, PREPROCESSOR_LIGHT, "Preprocessor", "PreprocessorSet");

        // Shared across definitions. Every definition names its own scopes, so a colour has to be
        // listed under each name that means the same thing; a name left out draws in the colour its
        // definition carries, which is what makes a language look untouched.
        Map(palette, COMMENT_DARK, COMMENT_LIGHT,
            "Comment", "DocCommentMarker", "CommentMarkerSet", "CommentTags", "JavaDocTags",
            "DocComment", "DocCommentSet", "KnownDocTags", "XmlPunctuation");
        Map(palette, STRING_DARK, STRING_LIGHT,
            "String", "Char", "Character", "StringInterpolation", "XmlString");
        Map(palette, NUMBER_DARK, NUMBER_LIGHT, "NumberLiteral", "Number", "Digits", "DateLiteral");
        Map(palette, CONTROL_DARK, CONTROL_LIGHT,
            "ControlFlow", "LoopKeywords", "JumpKeywords", "IterationStatements",
            "SelectionStatements", "JumpStatements", "ControlStatements", "ExceptionHandling",
            "ExceptionHandlingStatements", "CompoundKeywords");
        Map(palette, MODIFIER_DARK, MODIFIER_LIGHT,
            "AccessModifiers", "AccessKeywords", "Friend", "FunctionKeywords");
        Map(palette, TYPE_DARK, TYPE_LIGHT,
            "ValueTypes", "ReferenceTypes", "DataTypes", "OtherTypes", "Void");
        Map(palette, FUNCTION_DARK, FUNCTION_LIGHT, "MethodName", "FunctionCall", "Command");
        Map(palette, CONSTANT_DARK, CONSTANT_LIGHT, "Literals", "Constants", "BooleanConstants");
        Map(palette, PREPROCESSOR_DARK, PREPROCESSOR_LIGHT, "Package", "Namespace");
        Map(palette, LANGUAGE_VARIABLE_DARK, LANGUAGE_VARIABLE_LIGHT, "This");
        Map(palette, VARIABLE_DARK, VARIABLE_LIGHT, "Variable");
        Map(palette, OPERATOR_DARK, OPERATOR_LIGHT, "Operators");

        // XML.
        Map(palette, TAG_DARK, TAG_LIGHT, "XmlTag");
        Map(palette, ATTRIBUTE_DARK, ATTRIBUTE_LIGHT, "AttributeName");
        Map(palette, STRING_DARK, STRING_LIGHT, "AttributeValue");
        Map(palette, KEYWORD_DARK, KEYWORD_LIGHT, "Entity");
        Map(palette, INVALID_DARK, INVALID_LIGHT, "BrokenEntity");
        Map(palette, TAG_PUNCTUATION_DARK, TAG_PUNCTUATION_LIGHT, "CData", "DocType", "XmlDeclaration");

        // HTML and ASPX, which name the same things differently from XML.
        Map(palette, TAG_DARK, TAG_LIGHT, "HtmlTag", "Tags", "ASPSectionStartEndTags");
        Map(palette, ATTRIBUTE_DARK, ATTRIBUTE_LIGHT, "Attributes");
        Map(palette, INVALID_DARK, INVALID_LIGHT, "UnknownAttribute", "UnknownScriptTag");
        Map(palette, KEYWORD_DARK, KEYWORD_LIGHT, "Entities", "EntityReference", "EntityReferenceSet");
        Map(palette, TAG_PUNCTUATION_DARK, TAG_PUNCTUATION_LIGHT, "Assignment", "Slash");
        Map(palette, CONTROL_DARK, CONTROL_LIGHT,
            "ScriptTag", "JavaScriptTag", "VBScriptTag", "JScriptTag", "ASPSection");

        // CSS.
        Map(palette, SELECTOR_DARK, SELECTOR_LIGHT, "Selector", "Class");
        Map(palette, PROPERTY_DARK, PROPERTY_LIGHT, "Property");
        Map(palette, STRING_DARK, STRING_LIGHT, "Value");
        Map(palette, TAG_PUNCTUATION_DARK, TAG_PUNCTUATION_LIGHT, "Colon", "CurlyBraces");

        // Markdown.
        Map(palette, HEADING_DARK, HEADING_LIGHT, "Heading");
        Map(palette, EMPHASIS_DARK, EMPHASIS_LIGHT, "Emphasis", "StrongEmphasis");
        Map(palette, HEADING_DARK, HEADING_LIGHT, "Code");
        Map(palette, INSERTED_DARK, INSERTED_LIGHT, "BlockQuote");
        Map(palette, FUNCTION_DARK, FUNCTION_LIGHT, "Link", "Image");

        // Patch and diff.
        Map(palette, INSERTED_DARK, INSERTED_LIGHT, "AddedText");
        Map(palette, INVALID_DARK, INVALID_LIGHT, "RemovedText");
        Map(palette, HEADING_DARK, HEADING_LIGHT, "Header", "FileName");
        Map(palette, DIFF_RANGE_DARK, DIFF_RANGE_LIGHT, "Position");

        // JSON. The definition draws braces and brackets through Object/Array/Expression, which the
        // theme has no rule for, so those keep the definition's colour.
        Map(palette, PROPERTY_DARK, PROPERTY_LIGHT, "FieldName");
        Map(palette, CONSTANT_DARK, CONSTANT_LIGHT, "Bool", "Null");

        // JavaScript, for the HTML and script samples. Built-ins take the support colours the theme
        // keeps apart from user-defined names.
        Map(palette, CONTROL_DARK, CONTROL_LIGHT, "JavaScriptKeyWords");
        Map(palette, CONSTANT_DARK, CONSTANT_LIGHT, "JavaScriptLiterals");
        Map(palette, SUPPORT_FUNCTION_DARK, SUPPORT_FUNCTION_LIGHT, "JavaScriptGlobalFunctions");
        Map(palette, TYPE_DARK, TYPE_LIGHT, "JavaScriptIntrinsics");
        Map(palette, REGEX_DARK, REGEX_LIGHT, "Regex");

        // Escapes inside strings, where a definition names them (CSS, Java, PHP).
        Map(palette, ESCAPE_DARK, ESCAPE_LIGHT, "Escape", "EscapeSequence");

        HighlightingPalette.Current = palette;
    }

    /// <summary>
    /// Applies the surface colours the theme carries. Only these three are reachable: the selection,
    /// the caret and the current-line highlight come from the MewUI theme and the editor exposes no
    /// way to override them.
    /// </summary>
    public static void ApplyEditorColors(TextEditor editor, bool isDark)
    {
        editor.Background = Color.FromHex(isDark ? BACKGROUND_DARK : BACKGROUND_LIGHT);
        editor.Foreground = Color.FromHex(isDark ? FOREGROUND_DARK : FOREGROUND_LIGHT);
        editor.LineNumbersForeground = Color.FromHex(isDark ? LINE_NUMBER_DARK : LINE_NUMBER_LIGHT);
    }

    private static void Map(HighlightingPalette palette, string dark, string light, params string[] scopes)
    {
        var entry = new PaletteEntry(Color.FromHex(dark), Color.FromHex(light));
        foreach (string scope in scopes)
        {
            palette.Set(scope, entry);
        }
    }
}
