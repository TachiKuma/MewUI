using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class BindingErrorTransportTests
{
    [TestMethod]
    public void ConvertBackFailure_PreservesCandidateAndClearsOnSuccessfulCommit()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        var errors = new List<BindingError?>();
        target.ObserveErrors(errors.Add);
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        target.Commit("invalid");

        Assert.AreEqual("invalid", target.Text);
        Assert.AreEqual(1, source.Value);
        BindingStateSnapshot failed = target.GetState();
        Assert.AreEqual("invalid", failed.CurrentCandidate);
        Assert.AreEqual("1", failed.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, failed.Error?.Status);
        Assert.AreEqual(BindingErrorStage.ConvertBack, failed.Error?.Stage);

        target.Commit("2");

        Assert.AreEqual(2, source.Value);
        Assert.AreEqual("2", target.Text);
        BindingStateSnapshot recovered = target.GetState();
        Assert.AreEqual("2", recovered.LastSuccessfulTargetValue);
        Assert.IsNull(recovered.Error);
        Assert.HasCount(2, errors);
        Assert.IsNotNull(errors[0]);
        Assert.IsNull(errors[1]);
    }

    [TestMethod]
    public void SourceWriteFailure_DoesNotAssumeTheSourceWasRolledBack()
    {
        var source = new ObservableValue<int>(1);
        var target = new IntTarget();
        target.SetBinding(IntTarget.ValueProperty, source, BindingMode.TwoWay);
        source.Changed += static () => throw new InvalidOperationException("source observer failed");

        target.Commit(2);

        Assert.AreEqual(2, target.Value);
        Assert.AreEqual(2, source.Value, "the setter changed the source before an observer threw");
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual(2, state.CurrentCandidate);
        Assert.AreEqual(1, state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.BindingError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.SourceWrite, state.Error?.Stage);
    }

    [TestMethod]
    public void MewPropertySourceValidationFailure_IsRecoverableAndPrecedesTheWrite()
    {
        var source = new ValidatedSource { Value = 1 };
        var target = new IntTarget();
        target.SetBinding(
            IntTarget.ValueProperty,
            source,
            ValidatedSource.ValueProperty,
            static value => value,
            static value => value,
            BindingMode.TwoWay);

        target.Commit(-1);

        Assert.AreEqual(-1, target.Value);
        Assert.AreEqual(1, source.Value);
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual(-1, state.CurrentCandidate);
        Assert.AreEqual(1, state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.SourceValidation, state.Error?.Stage);
    }

    [TestMethod]
    public void ReadBackFailureAfterSourceWrite_IsReportedAsConsistencyError()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value == 2
                ? throw new InvalidOperationException("read-back conversion failed")
                : value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        target.Commit("2");

        Assert.AreEqual(2, source.Value);
        Assert.AreEqual("2", target.Text);
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual("2", state.CurrentCandidate);
        Assert.AreEqual("1", state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.BindingError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.Consistency, state.Error?.Stage);
    }

    [TestMethod]
    public void SourceConversionFailure_KeepsLastTargetAndRecoversOnNextPush()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value == 2
                ? throw new InvalidOperationException("conversion failed")
                : value.ToString(),
            mode: BindingMode.OneWay);

        source.Value = 2;

        Assert.AreEqual("1", target.Text);
        BindingStateSnapshot failed = target.GetState();
        Assert.AreEqual(2, failed.CurrentCandidate);
        Assert.AreEqual("1", failed.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingErrorStage.Convert, failed.Error?.Stage);

        source.Value = 3;

        Assert.AreEqual("3", target.Text);
        BindingStateSnapshot recovered = target.GetState();
        Assert.AreEqual("3", recovered.CurrentCandidate);
        Assert.AreEqual("3", recovered.LastSuccessfulTargetValue);
        Assert.IsNull(recovered.Error);
    }

    [TestMethod]
    public void TargetValidationFailure_LeavesSourceUnchangedAndTracksRejectedCandidate()
    {
        var source = new ObservableValue<int>(1);
        var target = new ValidatedTarget();
        target.SetBinding(ValidatedTarget.ValueProperty, source, BindingMode.TwoWay);

        target.Commit(-1);

        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(1, target.Value);
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual(-1, state.CurrentCandidate);
        Assert.AreEqual(1, state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.TargetValidation, state.Error?.Stage);
    }

    [TestMethod]
    public void ReplacingBinding_ClearsThePreviousErrorState()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(3);
        var target = new TextTarget();
        var errors = new List<BindingError?>();
        target.ObserveErrors(errors.Add);
        target.SetBinding(
            TextTarget.TextProperty,
            first,
            static value => value == 2
                ? throw new InvalidOperationException("conversion failed")
                : value.ToString(),
            mode: BindingMode.OneWay);
        first.Value = 2;

        target.SetBinding(
            TextTarget.TextProperty,
            second,
            static value => value.ToString(),
            mode: BindingMode.OneWay);

        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual("3", target.Text);
        Assert.AreEqual("3", state.LastSuccessfulTargetValue);
        Assert.IsNull(state.Error);
        Assert.HasCount(2, errors);
        Assert.IsNotNull(errors[0]);
        Assert.IsNull(errors[1]);
    }

    [TestMethod]
    public void ClearingBinding_RemovesItsErrorState()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        var errors = new List<BindingError?>();
        target.ObserveErrors(errors.Add);
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value == 2
                ? throw new InvalidOperationException("conversion failed")
                : value.ToString(),
            mode: BindingMode.OneWay);
        source.Value = 2;

        target.ClearBinding(TextTarget.TextProperty);

        Assert.IsNull(target.TryGetState());
        Assert.HasCount(2, errors);
        Assert.IsNotNull(errors[0]);
        Assert.IsNull(errors[1]);
    }

    private sealed class IntTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<IntTarget>(nameof(Value), 0);

        public int Value => GetValue(ValueProperty);

        public void Commit(int value) => CommitTargetValue(ValueProperty, value);

        public BindingStateSnapshot GetState() => GetBindingState(ValueProperty.Id)!.Value;
    }

    private sealed class TextTarget : MewObject
    {
        public static readonly MewProperty<string> TextProperty =
            MewProperty<string>.Register<TextTarget>(nameof(Text), string.Empty);

        public string Text => GetValue(TextProperty);

        public void Commit(string value) => CommitTargetValue(TextProperty, value);

        public BindingStateSnapshot GetState() => GetBindingState(TextProperty.Id)!.Value;

        public BindingStateSnapshot? TryGetState() => GetBindingState(TextProperty.Id);

        public void ObserveErrors(Action<BindingError?> callback)
            => AddBindingErrorChangedCallback(TextProperty.Id, callback);
    }

    private sealed class ValidatedTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<ValidatedTarget>(
                nameof(Value),
                0,
                validate: static (_, value) =>
                {
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }
                });

        public int Value => GetValue(ValueProperty);

        public void Commit(int value) => CommitTargetValue(ValueProperty, value);

        public BindingStateSnapshot GetState() => GetBindingState(ValueProperty.Id)!.Value;
    }

    private sealed class ValidatedSource : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<ValidatedSource>(
                nameof(Value),
                0,
                validate: static (_, value) =>
                {
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }
                });

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
