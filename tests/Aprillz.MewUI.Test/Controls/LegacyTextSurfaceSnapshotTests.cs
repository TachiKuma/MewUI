using System.Reflection;

using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// Freezes the public/protected surface of the legacy text input classes for the TextBase
/// rebuild (agent/textBase/plan.md). The guard tests fail if the frozen legacy classes drift
/// during the migration window. When the new hierarchy lands, the union of its declared
/// surfaces must be a superset of the frozen union: additions are allowed, removals require
/// an explicit per-symbol decision.
/// </summary>
[TestClass]
public sealed class LegacyTextSurfaceSnapshotTests
{
    [TestMethod]
    public void LegacyTextBase_MatchesFrozenSurface()
        => AssertSurface(typeof(LegacyTextBase), _legacyTextBaseSurface);

    [TestMethod]
    public void LegacySingleLineTextBase_MatchesFrozenSurface()
        => AssertSurface(typeof(LegacySingleLineTextBase), _legacySingleLineTextBaseSurface);

    [TestMethod]
    public void TextBox_CoversLegacyPublicSurface()
    {
        var chain = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in new[] { typeof(TextBox), typeof(SingleLineTextBase), typeof(TextBase) })
        {
            foreach (var entry in GetDeclaredSurface(type, publicOnly: true))
            {
                chain.Add(StripInheritanceModifiers(entry));
            }
        }

        var missing = _textBoxPublicSurface.Where(entry => !chain.Contains(entry)).ToList();
        Assert.IsTrue(missing.Count == 0,
            $"Rebuilt TextBox chain lost legacy public surface.\nMissing:\n  {string.Join("\n  ", missing)}");
    }

    private static string StripInheritanceModifiers(string entry)
        => entry.Replace(":abstract", "").Replace(":virtual", "");

    [TestMethod]
    public void PasswordBox_CoversLegacyPublicSurface()
    {
        var chain = GetPublicChainSurface(typeof(PasswordBox), typeof(SingleLineTextBase), typeof(TextBase));

        var missing = _passwordBoxPublicSurface.Where(entry => !chain.Contains(entry)).ToList();
        Assert.IsTrue(missing.Count == 0,
            $"Rebuilt PasswordBox chain lost legacy public surface.\nMissing:\n  {string.Join("\n  ", missing)}");

        // Deferral design: the password surface must never expose document text publicly.
        Assert.IsFalse(chain.Any(entry => entry.StartsWith("P:Text:", StringComparison.Ordinal)),
            "PasswordBox chain must not expose a public Text property");
        Assert.IsFalse(chain.Any(entry => entry.StartsWith("P:SelectedText:", StringComparison.Ordinal)),
            "PasswordBox chain must not expose a public SelectedText property");
        Assert.IsFalse(chain.Any(entry => entry.StartsWith("F:TextProperty:", StringComparison.Ordinal)),
            "PasswordBox chain must not expose a public TextProperty field");
    }

    private static HashSet<string> GetPublicChainSurface(params Type[] chainTypes)
    {
        var chain = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in chainTypes)
        {
            foreach (var entry in GetDeclaredSurface(type, publicOnly: true))
            {
                chain.Add(StripInheritanceModifiers(entry));
            }
        }
        return chain;
    }

    private static void AssertSurface(Type type, string[] frozen)
    {
        var actual = GetDeclaredSurface(type);
        var missing = frozen.Except(actual).ToList();
        var added = actual.Except(frozen).ToList();
        Assert.IsTrue(missing.Count == 0 && added.Count == 0,
            $"{type.Name} surface drifted.\nMissing:\n  {string.Join("\n  ", missing)}\nAdded:\n  {string.Join("\n  ", added)}");
    }

    /// <summary>
    /// Formats the declared public/protected members of a type into stable snapshot entries.
    /// Declaring-type names are excluded so the entries survive class renames.
    /// </summary>
    internal static HashSet<string> GetDeclaredSurface(Type type, bool publicOnly = false)
    {
        const BindingFlags FLAGS = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var entries = new HashSet<string>(StringComparer.Ordinal);

        bool Visible(MethodBase? method) => method != null && (publicOnly
            ? method.IsPublic
            : method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);
        static string TypeName(Type memberType) => memberType.IsGenericType
            ? $"{memberType.Name.Split('`')[0]}<{string.Join(",", memberType.GetGenericArguments().Select(TypeName))}>"
            : memberType.Name;
        static string Mod(MethodBase method) => method.IsAbstract ? ":abstract" : (method.IsVirtual && !method.IsFinal ? ":virtual" : "");
        static string ParameterList(MethodBase method) => string.Join(",", method.GetParameters().Select(parameter => TypeName(parameter.ParameterType)));

        foreach (var field in type.GetFields(FLAGS))
        {
            bool fieldVisible = publicOnly
                ? field.IsPublic
                : field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
            if (!fieldVisible) continue;
            entries.Add($"F:{field.Name}:{TypeName(field.FieldType)}");
        }

        foreach (var constructor in type.GetConstructors(FLAGS))
        {
            if (!Visible(constructor)) continue;
            entries.Add($"C:({ParameterList(constructor)})");
        }

        foreach (var property in type.GetProperties(FLAGS))
        {
            var hasGet = Visible(property.GetMethod);
            var hasSet = Visible(property.SetMethod);
            if (!hasGet && !hasSet) continue;
            var accessor = hasGet ? property.GetMethod! : property.SetMethod!;
            entries.Add($"P:{property.Name}:{TypeName(property.PropertyType)}:{(hasGet ? "get" : "")}{(hasSet ? "set" : "")}{Mod(accessor)}");
        }

        foreach (var eventInfo in type.GetEvents(FLAGS))
        {
            if (!Visible(eventInfo.AddMethod)) continue;
            entries.Add($"E:{eventInfo.Name}:{TypeName(eventInfo.EventHandlerType!)}");
        }

        foreach (var method in type.GetMethods(FLAGS))
        {
            if (!Visible(method) || method.IsSpecialName) continue;
            entries.Add($"M:{method.Name}({ParameterList(method)}):{TypeName(method.ReturnType)}{Mod(method)}");
        }

        return entries;
    }

    // Snapshot taken 2026-08-02 right after the Legacy* rename (rename does not affect member surface).
    private static readonly string[] _legacyTextBaseSurface =
    {
        "C:()",
        "E:TextChanged:Action<String>",
        "E:TextCompositionEnd:Action<TextCompositionEventArgs>",
        "E:TextCompositionStart:Action<TextCompositionEventArgs>",
        "E:TextCompositionUpdate:Action<TextCompositionEventArgs>",
        "E:TextInput:Action<TextInputEventArgs>",
        "E:WrapChanged:Action<Boolean>",
        "F:AcceptTabProperty:MewProperty<Boolean>",
        "F:ImeModeProperty:MewProperty<ImeMode>",
        "F:IsReadOnlyProperty:MewProperty<Boolean>",
        "F:MaxLengthProperty:MewProperty<Int32>",
        "F:PlaceholderProperty:MewProperty<String>",
        "F:SelectionLengthProperty:MewProperty<Int32>",
        "F:SelectionStartProperty:MewProperty<Int32>",
        "M:AdjustViewportBoundsForScrollbars(Rect,Theme):Rect:virtual",
        "M:AppendText(String,Boolean):Void",
        "M:ApplyExternalTextChange(String):Void",
        "M:ApplyExternalTextPropertyChange(String):Void",
        "M:ApplyInsertCore(Int32,ReadOnlySpan<Char>):Int32",
        "M:ApplyInsertForEdit(Int32,String):Void:virtual",
        "M:ApplyRemoveCore(Int32,Int32):Int32",
        "M:ApplyRemoveForEdit(Int32,Int32):Void:virtual",
        "M:AutoScrollForSelectionDrag(Point,Rect):Void:virtual",
        "M:BackspaceForEdit(Boolean):Void",
        "M:BumpDocumentVersion():Void",
        "M:ClampOffset(Double,Double,Double):Double",
        "M:ClampOffset(Double,Double,Double,Double):Double",
        "M:ClearUndoRedo():Void",
        "M:Copy():Void",
        "M:CopyToClipboardCore():Void:virtual",
        "M:Cut():Void",
        "M:CutToClipboardCore():Void:virtual",
        "M:DeleteForEdit(Boolean):Void",
        "M:DeleteSelectionForEdit():Boolean:virtual",
        "M:EnsureCaretVisibleCore(Rect):Void:virtual",
        "M:FindNextWordBoundary(Int32):Int32",
        "M:FindPreviousWordBoundary(Int32):Int32",
        "M:GetCharRectInWindow(Int32):Rect:abstract",
        "M:GetInteractionContentBounds():Rect:virtual",
        "M:GetSelectionRange():ValueTuple<Int32,Int32>",
        "M:GetTextCharCore(Int32):Char:virtual",
        "M:GetTextCore():String:virtual",
        "M:GetTextInnerBounds():Rect",
        "M:GetTextLengthCore():Int32:virtual",
        "M:GetTextSubstringCore(Int32,Int32):String:virtual",
        "M:GetViewportContentBounds():Rect",
        "M:GetViewportInnerBounds():Rect",
        "M:HitTestOverride(Point):UIElement:virtual",
        "M:InsertIntoDocument(Int32,ReadOnlySpan<Char>):Void",
        "M:InsertTextAtCaretForEdit(String):Void:virtual",
        "M:MoveCaretHorizontal(Int32,Boolean,Boolean):Void",
        "M:MoveCaretHorizontalKey(Int32,Boolean,Boolean):Void:virtual",
        "M:MoveCaretToDocumentEdge(Boolean,Boolean):Void",
        "M:MoveCaretToLineEdge(Boolean,Boolean):Void:virtual",
        "M:MoveCaretVerticalKey(Int32,Boolean):Void:virtual",
        "M:NormalizePastedText(String):String:virtual",
        "M:NormalizeText(String):String:virtual",
        "M:NotifyTextChanged():Void:virtual",
        "M:NotifyWrapChanged(Boolean):Void",
        "M:OnDispose():Void:virtual",
        "M:OnEditCommitted():Void:virtual",
        "M:OnGotFocus():Void:virtual",
        "M:OnHitTest(Point):UIElement:virtual",
        "M:OnKeyDown(KeyEventArgs):Void:virtual",
        "M:OnLostFocus():Void:virtual",
        "M:OnMouseDoubleClick(MouseEventArgs):Void:virtual",
        "M:OnMouseDown(MouseEventArgs):Void:virtual",
        "M:OnMouseMove(MouseEventArgs):Void:virtual",
        "M:OnMouseUp(MouseEventArgs):Void:virtual",
        "M:OnRender(IGraphicsContext):Void",
        "M:OnTextChanged(String,String):Void:virtual",
        "M:OnTextCompositionEnd(TextCompositionEventArgs):Void:virtual",
        "M:OnTextCompositionStart(TextCompositionEventArgs):Void:virtual",
        "M:OnTextCompositionUpdate(TextCompositionEventArgs):Void:virtual",
        "M:OnTextInput(TextInputEventArgs):Void:virtual",
        "M:OnWrapChanged(Boolean,Boolean):Void:virtual",
        "M:Paste():Void",
        "M:PasteFromClipboardCore():Void:virtual",
        "M:RaiseTextChanged():Void:virtual",
        "M:Redo():Void",
        "M:RemoveFromDocument(Int32,Int32):Void",
        "M:RenderAfterContent(IGraphicsContext,Theme,VisualState&):Void:virtual",
        "M:RenderTextContent(IGraphicsContext,Rect,IFont,Theme,VisualState&):Void:abstract",
        "M:ScrollToCaret():Void",
        "M:SelectAll():Void",
        "M:SelectAllCore():Void:virtual",
        "M:SetCaretAndSelection(Int32,Boolean):Void",
        "M:SetCaretFromPoint(Point,Rect):Void:abstract",
        "M:SetHorizontalOffset(Double,Boolean):Void",
        "M:SetMirroredTextProperty(MewProperty<String>,String):Void",
        "M:SetScrollOffsets(Double,Double,Boolean):Void",
        "M:SetTextCore(String):Void:virtual",
        "M:SetVerticalOffset(Double,Boolean):Void",
        "M:SetWrapEnabled(Boolean):Void",
        "M:SyncTextPropertyFromDocument(MewProperty<String>):Void",
        "M:TryClipboardGetText(String&):Boolean",
        "M:TryClipboardSetText(String):Boolean",
        "M:Undo():Void",
        "P:AcceptReturn:Boolean:getset",
        "P:AcceptTab:Boolean:getset",
        "P:CanRedo:Boolean:get",
        "P:CanUndo:Boolean:get",
        "P:CaretPosition:Int32:getset",
        "P:CompositionAttributes:CompositionAttr[]:get",
        "P:CompositionLength:Int32:get",
        "P:CompositionStartIndex:Int32:get",
        "P:DocumentVersion:Int32:get",
        "P:HasSelection:Boolean:get",
        "P:HorizontalOffset:Double:get",
        "P:ImeMode:ImeMode:getset",
        "P:IsComposing:Boolean:get",
        "P:IsReadOnly:Boolean:getset",
        "P:IsSelectionActive:Boolean:get",
        "P:IsSyncingTextProperty:Boolean:get",
        "P:MaxLength:Int32:getset",
        "P:Placeholder:String:getset",
        "P:PlaceholderVerticalAlignment:TextAlignment:get:virtual",
        "P:SelectedText:String:get:virtual",
        "P:SelectionLength:Int32:get",
        "P:SelectionStart:Int32:get",
        "P:SupportsWrap:Boolean:get:virtual",
        "P:VerticalOffset:Double:get",
        "P:WrapEnabled:Boolean:get",
    };

    private static readonly string[] _legacySingleLineTextBaseSurface =
    {
        "C:()",
        "M:AutoScrollForSelectionDrag(Point,Rect):Void:virtual",
        "M:CopyDocumentTo(Char[],Int32,Int32):Void:virtual",
        "M:EnsureCaretVisibleCore(Rect):Void:virtual",
        "M:GetCharRectInWindow(Int32):Rect:virtual",
        "M:GetInteractionContentBounds():Rect:virtual",
        "M:MeasureContent(Size):Size:virtual",
        "M:NormalizePastedText(String):String:virtual",
        "M:RenderTextContent(IGraphicsContext,Rect,IFont,Theme,VisualState&):Void:virtual",
        "M:SetCaretFromPoint(Point,Rect):Void:virtual",
    };

    // Legacy public surface of TextBox and its legacy base chain, minus decided removals
    // (WrapChanged: multiline-only; see agent/textBase/plan.md Breaking Changes).
    // Inheritance modifiers are stripped because virtual-ness is free to change in the rebuild.
    private static readonly string[] _textBoxPublicSurface =
    {
        "C:()",
        "E:TextChanged:Action<String>",
        "E:TextCompositionEnd:Action<TextCompositionEventArgs>",
        "E:TextCompositionStart:Action<TextCompositionEventArgs>",
        "E:TextCompositionUpdate:Action<TextCompositionEventArgs>",
        "E:TextInput:Action<TextInputEventArgs>",
        "F:AcceptTabProperty:MewProperty<Boolean>",
        "F:ImeModeProperty:MewProperty<ImeMode>",
        "F:IsReadOnlyProperty:MewProperty<Boolean>",
        "F:MaxLengthProperty:MewProperty<Int32>",
        "F:PlaceholderProperty:MewProperty<String>",
        "F:SelectionLengthProperty:MewProperty<Int32>",
        "F:SelectionStartProperty:MewProperty<Int32>",
        "F:TextProperty:MewProperty<String>",
        "M:AppendText(String,Boolean):Void",
        "M:Copy():Void",
        "M:Cut():Void",
        "M:GetCharRectInWindow(Int32):Rect",
        "M:Paste():Void",
        "M:Redo():Void",
        "M:ScrollToCaret():Void",
        "M:SelectAll():Void",
        "M:Undo():Void",
        "P:AcceptTab:Boolean:getset",
        "P:CanRedo:Boolean:get",
        "P:CanUndo:Boolean:get",
        "P:CaretPosition:Int32:getset",
        "P:ImeMode:ImeMode:getset",
        "P:IsReadOnly:Boolean:getset",
        "P:MaxLength:Int32:getset",
        "P:Placeholder:String:getset",
        "P:SelectedText:String:get",
        "P:SelectionLength:Int32:get",
        "P:SelectionStart:Int32:get",
        "P:Text:String:getset",
    };

    // Legacy public surface of PasswordBox and its legacy base chain, minus decided removals
    // (SelectedText and WrapChanged; see agent/textBase/plan.md Breaking Changes).
    private static readonly string[] _passwordBoxPublicSurface =
    {
        "C:()",
        "E:PasswordChanged:Action",
        "E:TextCompositionEnd:Action<TextCompositionEventArgs>",
        "E:TextCompositionStart:Action<TextCompositionEventArgs>",
        "E:TextCompositionUpdate:Action<TextCompositionEventArgs>",
        "E:TextInput:Action<TextInputEventArgs>",
        "F:AcceptTabProperty:MewProperty<Boolean>",
        "F:ImeModeProperty:MewProperty<ImeMode>",
        "F:IsReadOnlyProperty:MewProperty<Boolean>",
        "F:MaxLengthProperty:MewProperty<Int32>",
        "F:PasswordCharProperty:MewProperty<Char>",
        "F:PasswordProperty:MewProperty<String>",
        "F:PlaceholderProperty:MewProperty<String>",
        "F:SelectionLengthProperty:MewProperty<Int32>",
        "F:SelectionStartProperty:MewProperty<Int32>",
        "M:AppendText(String,Boolean):Void",
        "M:Copy():Void",
        "M:Cut():Void",
        "M:GetCharRectInWindow(Int32):Rect",
        "M:Paste():Void",
        "M:Redo():Void",
        "M:ScrollToCaret():Void",
        "M:SelectAll():Void",
        "M:Undo():Void",
        "P:AcceptTab:Boolean:getset",
        "P:CanRedo:Boolean:get",
        "P:CanUndo:Boolean:get",
        "P:CaretPosition:Int32:getset",
        "P:ImeMode:ImeMode:getset",
        "P:IsReadOnly:Boolean:getset",
        "P:MaxLength:Int32:getset",
        "P:Password:String:getset",
        "P:PasswordChar:Char:getset",
        "P:Placeholder:String:getset",
        "P:SelectionLength:Int32:get",
        "P:SelectionStart:Int32:get",
    };
}
