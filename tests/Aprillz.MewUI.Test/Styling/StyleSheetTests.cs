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
    public void Freeze_DoesNotMaterializeNamedFactories_AndRejectsFurtherDefinitions()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("lazy", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });

        sheet.Freeze();

        Assert.IsTrue(sheet.IsFrozen);
        Assert.AreEqual(0, calls);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sheet.Define("other", () => new Style(typeof(Button))));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sheet.Define<Button>(new Style(typeof(Button))));
        Assert.IsNotNull(sheet.Get("lazy"));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void OrdinaryLookup_DoesNotFreezeAStillConfigurableSheet()
    {
        var sheet = new StyleSheet();
        sheet.Define("first", () => new Style(typeof(Button)));

        Assert.IsNotNull(sheet.Get("first"));

        Assert.IsFalse(sheet.IsFrozen);
        sheet.Define("second", () => new Style(typeof(Button)));
        Assert.IsNotNull(sheet.Get("second"));
    }

    [TestMethod]
    public async Task DefineFactory_ConcurrentFirstLookup_MaterializesExactlyOnce()
    {
        var sheet = new StyleSheet();
        var expected = new Style(typeof(Button));
        var calls = 0;
        sheet.Define("shared", () =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(20);
            return expected;
        });
        sheet.Freeze();

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => sheet.Get("shared")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.AreEqual(1, calls);
        Assert.IsTrue(results.All(style => ReferenceEquals(expected, style)));
    }

    [TestMethod]
    public void DefineFactory_FailureIsCachedPerName()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("broken", () =>
        {
            calls++;
            throw new InvalidOperationException("factory failed");
        });
        sheet.Freeze();

        var first = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("broken"));
        var second = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("broken"));

        Assert.AreEqual(1, calls);
        Assert.AreEqual("factory failed", first.Message);
        Assert.AreEqual(first.Message, second.Message);
    }

    [TestMethod]
    public void DefineFactory_ReentrantLookupFailsAndCachesTheFailure()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("cycle", () =>
        {
            calls++;
            return sheet.Get("cycle")!;
        });
        sheet.Freeze();

        var first = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("cycle"));
        var second = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("cycle"));

        Assert.AreEqual(1, calls);
        StringAssert.Contains(first.Message, "recursively requested itself");
        Assert.AreEqual(first.Message, second.Message);
    }

    [TestMethod]
    public void InvalidateLazyCache_RecreatesNamedStyleButKeepsSheetFrozen()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("reloadable", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });
        sheet.Freeze();
        var first = sheet.Get("reloadable");

        sheet.InvalidateLazyCache();
        var second = sheet.Get("reloadable");

        Assert.IsTrue(sheet.IsFrozen);
        Assert.AreEqual(2, calls);
        Assert.AreNotSame(first, second);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sheet.Define("late", () => new Style(typeof(Button))));
    }

    [TestMethod]
    public void InvalidateLazyCache_AllowsFailedFactoryToRetryAfterHotReload()
    {
        var sheet = new StyleSheet();
        var shouldFail = true;
        var calls = 0;
        sheet.Define("reloadable", () =>
        {
            calls++;
            if (shouldFail)
            {
                throw new InvalidOperationException("old failure");
            }

            return new Style(typeof(Button));
        });
        sheet.Freeze();
        Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("reloadable"));

        shouldFail = false;
        sheet.InvalidateLazyCache();

        Assert.IsNotNull(sheet.Get("reloadable"));
        Assert.AreEqual(2, calls);
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
