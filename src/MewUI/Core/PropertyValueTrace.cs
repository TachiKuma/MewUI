namespace Aprillz.MewUI;

internal readonly record struct PropertyValueCandidateTrace(
    ValueSource Source,
    bool IsSet,
    bool IsWinner,
    object? RawValue);

internal readonly record struct PropertyValueTrace(
    MewProperty Property,
    object? BaseValue,
    object? VisualValue,
    ValueSource EffectiveSource,
    bool IsAnimated,
    PropertyValueCandidateTrace Local,
    PropertyValueCandidateTrace Trigger,
    PropertyValueCandidateTrace Binding,
    PropertyValueCandidateTrace Style,
    PropertyValueCandidateTrace Inherited,
    PropertyValueCandidateTrace Default,
    BindingStateSnapshot? BindingState)
{
    public bool HasNonDefaultCandidate
        => Local.IsSet || Trigger.IsSet || Binding.IsSet || Style.IsSet || Inherited.IsSet;

    public PropertyValueCandidateTrace GetCandidate(ValueSource source)
        => source switch
        {
            ValueSource.Local => Local,
            ValueSource.Trigger => Trigger,
            ValueSource.Binding => Binding,
            ValueSource.Style => Style,
            ValueSource.Inherited => Inherited,
            ValueSource.Default => Default,
            _ => new PropertyValueCandidateTrace(source, false, false, null),
        };
}
