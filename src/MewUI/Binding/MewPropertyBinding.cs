using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Bridges a <see cref="MewProperty{T}"/> on a <see cref="MewObject"/> to an <see cref="ObservableValue{T}"/>.
/// Handles cycle prevention automatically via a re-entrancy guard.
/// </summary>
internal sealed class MewPropertyBinding<T> : IDisposable
{
    private readonly MewObject _owner;
    private readonly MewProperty<T> _property;
    private readonly ObservableValue<T> _source;
    private readonly BindingCapabilities _capabilities;
    private readonly Action? _onPropertyChanged;
    private bool _updating;

    public MewPropertyBinding(
        MewObject owner,
        MewProperty<T> property,
        ObservableValue<T> source,
        BindingMode mode)
    {
        _owner = owner;
        _property = property;
        _source = source;
        _capabilities = BindingCapabilities.FromMode(mode);

        if (_capabilities.ObservesSourceChanges)
        {
            WeakEventManager.AddHandler(
                ObservableValueWeakEvents<T>.Changed,
                source,
                this,
                static binding => binding.OnSourceChanged());
        }

        if (_capabilities.AcceptsTargetCommit)
        {
            _onPropertyChanged = OnPropertyChanged;
            owner.AddPropertyBindingCallback(property.Id, _onPropertyChanged);
        }

        // Initial sync from source.
        if (_capabilities.ProvidesTargetValue)
        {
            OnSourceChanged();
        }
    }

    private void OnSourceChanged()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            var value = _source.Value;
            if (!EqualityComparer<T>.Default.Equals(_owner.GetBindingValue(_property), value))
            {
                _owner.SetBindingValue(_property, value);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnPropertyChanged()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            _source.Value = _owner.GetBindingValue(_property);
        }
        finally
        {
            _updating = false;
        }
    }

    public void Dispose()
    {
        if (_capabilities.ObservesSourceChanges)
        {
            WeakEventManager.RemoveHandler(ObservableValueWeakEvents<T>.Changed, _source, this);
        }

        if (_capabilities.AcceptsTargetCommit && _onPropertyChanged != null)
        {
            _owner.RemovePropertyBindingCallback(_property.Id, _onPropertyChanged);
        }
    }
}
