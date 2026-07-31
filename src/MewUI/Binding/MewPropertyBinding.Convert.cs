using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Bridges a <see cref="MewProperty{TProp}"/> to an <see cref="ObservableValue{TSource}"/>
/// when the types differ, applying convert/convertBack functions.
/// </summary>
internal sealed class MewPropertyBinding<TProp, TSource> : IPropertyBinding
{
    private readonly MewObject _owner;
    private readonly MewProperty<TProp> _property;
    private readonly ObservableValue<TSource> _source;
    private readonly Func<TSource, TProp> _convert;
    private readonly Func<TProp, TSource>? _convertBack;
    private readonly BindingCapabilities _capabilities;
    private readonly Action? _onPropertyChanged;
    private bool _updating;

    public BindingCapabilities Capabilities => _capabilities;

    public MewPropertyBinding(
        MewObject owner,
        MewProperty<TProp> property,
        ObservableValue<TSource> source,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack,
        BindingMode mode)
    {
        _owner = owner;
        _property = property;
        _source = source;
        _convert = convert;
        _convertBack = convertBack;
        _capabilities = BindingCapabilities.FromMode(mode);

        if (_capabilities.ObservesSourceChanges)
        {
            WeakEventManager.AddHandler(
                ObservableValueWeakEvents<TSource>.Changed,
                source,
                this,
                static binding => binding.OnSourceChanged());
        }

        if (_capabilities.AcceptsTargetCommit && convertBack != null)
        {
            _onPropertyChanged = OnPropertyChanged;
            owner.AddPropertyBindingCallback(property.Id, _onPropertyChanged);
        }

        if (_capabilities.ProvidesTargetValue)
        {
            OnSourceChanged();
        }
    }

    private void OnSourceChanged()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var converted = _convert(_source.Value);
            if (!EqualityComparer<TProp>.Default.Equals(
                    _owner.GetBindingValue(_property), converted))
            {
                _owner.UpdateBindingTarget(_property, converted);
            }
        }
        finally { _updating = false; }
    }

    private void OnPropertyChanged()
    {
        if (_updating || _convertBack == null) return;
        _updating = true;
        try
        {
            _source.Value = _convertBack(_owner.GetBindingValue(_property));
        }
        finally { _updating = false; }
    }

    public void UpdateTargetValue(object? value)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            _owner.UpdateBindingTarget(_property, (TProp)value!);
        }
        finally { _updating = false; }
    }

    public void Dispose()
    {
        if (_capabilities.ObservesSourceChanges)
        {
            WeakEventManager.RemoveHandler(ObservableValueWeakEvents<TSource>.Changed, _source, this);
        }
        if (_capabilities.AcceptsTargetCommit && _onPropertyChanged != null)
        {
            _owner.RemovePropertyBindingCallback(_property.Id, _onPropertyChanged);
        }
    }
}
