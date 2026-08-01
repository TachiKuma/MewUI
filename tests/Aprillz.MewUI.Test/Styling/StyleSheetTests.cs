using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Styling;

[TestClass]
public sealed class StyleSheetTests
{
    [TestMethod]
    public void DefineFactory_DoesNotCreateStyleUntilLookup()
    {
        var sheet = new StyleSheet();
        var style = new Style(typeof(Button));
        var calls = 0;

        sheet.Define("lazy", () =>
        {
            calls++;
            return style;
        });

        Assert.AreEqual(0, calls);

        Assert.AreSame(style, sheet.Get("lazy"));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DefineFactory_CachesCreatedStyle()
    {
        var sheet = new StyleSheet();
        var calls = 0;

        sheet.Define("lazy", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });

        var first = sheet.Get("lazy");
        var second = sheet.Get("lazy");

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DefineFactory_ReplacesPendingFactory()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        var replacement = new Style(typeof(Button));

        sheet.Define("style", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });
        sheet.Define("style", () => replacement);

        Assert.AreSame(replacement, sheet.Get("style"));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void GetByType_SelectsNearestBaseRegardlessOfRegistrationOrder()
    {
        var sheet = new StyleSheet();
        var nearest = new Style(typeof(LookupBase));
        var farther = new Style(typeof(Control));
        sheet.Define<LookupBase>(nearest);
        sheet.Define<Control>(farther);

        Assert.AreSame(nearest, sheet.GetByType(typeof(LookupDerived)));
    }

    [TestMethod]
    public void GetByType_UsesTheLastRuleForTheSameType()
    {
        var sheet = new StyleSheet();
        var first = new Style(typeof(LookupBase));
        var second = new Style(typeof(LookupBase));
        sheet.Define<LookupBase>(first);
        sheet.Define<LookupBase>(second);

        Assert.AreSame(second, sheet.GetByType(typeof(LookupDerived)));
    }

    [TestMethod]
    public void StyleAndTypeLookup_RejectNonControlTypes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Style(typeof(string)));
        Assert.ThrowsExactly<ArgumentException>(() => Style.ForType(typeof(string)));

        var sheet = new StyleSheet();
        Assert.ThrowsExactly<ArgumentException>(() => sheet.GetByType(typeof(string)));
    }

    [TestMethod]
    public void DefineTypeRule_RejectsAnIncompatibleStyleTarget()
    {
        var sheet = new StyleSheet();

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => sheet.Define<LookupBase>(new Style(typeof(Button))));

        StringAssert.Contains(exception.Message, typeof(Button).FullName!);
        StringAssert.Contains(exception.Message, typeof(LookupBase).FullName!);
    }

    private class LookupBase : Control
    {
    }

    private sealed class LookupDerived : LookupBase
    {
    }
}
