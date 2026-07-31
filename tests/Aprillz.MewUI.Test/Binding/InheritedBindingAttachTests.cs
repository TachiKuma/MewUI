using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Binding;

/// <summary>
/// A binding sourced from an inherited property captures whatever the source resolves to when the
/// binding is created, which for a detached element is the registered default. Attaching gives the
/// subtree a new inherited context, so the binding must be re-evaluated then: nothing re-reads a
/// property whose value is pushed rather than pulled.
/// </summary>
[TestClass]
public sealed class InheritedBindingAttachTests
{
    private static readonly Color FIRST = Color.FromRgb(10, 20, 30);
    private static readonly Color SECOND = Color.FromRgb(200, 210, 220);

    private static PathShape BoundIcon()
    {
        var icon = new PathShape { Data = PathGeometry.Parse("M 0 0 L 8 0 L 8 8 Z") };
        icon.Bind(Shape.FillProperty, icon, TextElement.ForegroundProperty,
            (Color color) => (Brush)new SolidColorBrush(color));
        return icon;
    }

    private static Color FillColor(PathShape icon)
        => icon.Fill is SolidColorBrush solid ? solid.Color : default;

    [TestMethod]
    public void Binding_TakesInheritedValue_OnAttach()
    {
        var icon = BoundIcon();
        var host = new Border { Foreground = FIRST };

        host.Child = icon;

        Assert.AreEqual(FIRST, FillColor(icon));
    }

    [TestMethod]
    public void Binding_PicksUpAncestorChange_MadeWhileDetached()
    {
        var icon = BoundIcon();
        var host = new Border { Foreground = FIRST };
        host.Child = icon;

        host.Child = null;
        host.Foreground = SECOND;
        host.Child = icon;

        Assert.AreEqual(SECOND, FillColor(icon));
    }

    [TestMethod]
    public void Binding_FollowsAncestorChange_WhileAttached()
    {
        var icon = BoundIcon();
        var host = new Border { Foreground = FIRST };
        host.Child = icon;

        host.Foreground = SECOND;

        Assert.AreEqual(SECOND, FillColor(icon));
    }
}
