using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class BindingValueSourceCharacterizationTests
{
    [TestMethod]
    public void BindingPushAndDirectWrite_UseDifferentSources()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        Assert.AreEqual(ValueSource.Binding, target.PropertyStore.GetSource(Target.ValueProperty.Id));

        target.Value = 2;

        Assert.AreEqual(ValueSource.Local, target.PropertyStore.GetSource(Target.ValueProperty.Id));
    }

    [TestMethod]
    public void OneWayBinding_LocalOverrideHidesButDoesNotStaleBindingValue()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        target.Value = 2;
        Assert.AreEqual(2, target.Value);

        source.Value = 3;
        Assert.AreEqual(2, target.Value, "Local remains the effective source");

        target.PropertyStore.ClearLocal(Target.ValueProperty);
        Assert.AreEqual(3, target.Value, "clearing Local reveals the latest Binding candidate");
    }

    [TestMethod]
    public void ObservableBinding_ClearBindingRemovesItsValueSlot()
    {
        var source = new ObservableValue<int>(4);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.OneWay);

        target.ClearBinding(Target.ValueProperty);
        source.Value = 5;

        Assert.AreEqual(0, target.Value);
    }

    [TestMethod]
    public void ObservableBinding_ReplacementDetachesThePreviousSource()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(2);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, first, BindingMode.OneWay);

        target.SetBinding(Target.ValueProperty, second, BindingMode.OneWay);
        first.Value = 3;
        Assert.AreEqual(2, target.Value);

        second.Value = 4;
        Assert.AreEqual(4, target.Value);
    }

    [TestMethod]
    public void ObservableTwoWayBinding_DirectWriteUpdatesSourceExactlyOnce()
    {
        var source = new ObservableValue<int>(1);
        var sourceChangeCount = 0;
        source.Changed += () => sourceChangeCount++;
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.Value = 2;

        Assert.AreEqual(2, source.Value);
        Assert.AreEqual(1, sourceChangeCount);
    }

    [TestMethod]
    public void ObservableTwoWayBinding_EqualDirectWriteDoesNotUpdateSource()
    {
        var source = new ObservableValue<int>(1);
        var sourceChangeCount = 0;
        source.Changed += () => sourceChangeCount++;
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.Value = 1;

        Assert.AreEqual(0, sourceChangeCount);
    }

    [TestMethod]
    public void ObservableTwoWayBinding_SourceCoercionDoesNotRoundTripToTargetDuringWriteBack()
    {
        var source = new ObservableValue<int>(1, static value => Math.Clamp(value, 0, 10));
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.Value = 99;

        Assert.AreEqual(10, source.Value, "the source coerces the submitted target value");
        Assert.AreEqual(
            99,
            target.Value,
            "the current re-entrancy guard suppresses the normalized source push back to the target");
    }

    [TestMethod]
    public void ConvertedObservableTwoWayBinding_SourceCoercionDoesNotRoundTripToTarget()
    {
        var source = new ObservableValue<int>(1, static value => Math.Clamp(value, 0, 10));
        var target = new TextTarget();
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        target.Text = "99";

        Assert.AreEqual(10, source.Value);
        Assert.AreEqual(
            "99",
            target.Text,
            "the converted Observable binding has the same suppressed normalization round trip");
    }

    [TestMethod]
    public void ClearObservableTwoWayBinding_RemovesWriteBackCallback()
    {
        var source = new ObservableValue<int>(1);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, source, BindingMode.TwoWay);

        target.ClearBinding(Target.ValueProperty);
        target.Value = 2;

        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(2, target.Value);
    }

    private sealed class Target : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<Target>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class TextTarget : MewObject
    {
        public static readonly MewProperty<string> TextProperty =
            MewProperty<string>.Register<TextTarget>(nameof(Text), string.Empty);

        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }
}
