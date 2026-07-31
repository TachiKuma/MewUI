using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal sealed class MewPropertyPathBinding<TProp, TRoot, TSource> : IPropertyBinding
    where TRoot : class
{
    private readonly MewObject _target;
    private readonly MewProperty<TProp> _targetProperty;
    private readonly BindingPathObserver<TRoot, TSource> _observer;
    private readonly Func<TSource, TProp> _convert;
    private readonly Func<TProp, TSource>? _convertBack;
    private readonly TProp _fallbackValue;
    private readonly BindingCapabilities _capabilities;
    private readonly Action? _onTargetChanged;
    private bool _updating;
    private bool _disposed;

    public BindingCapabilities Capabilities => _capabilities;

    internal MewPropertyPathBinding(
        MewObject target,
        MewProperty<TProp> targetProperty,
        TRoot root,
        BindingPath<TRoot, TSource> path,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack,
        BindingMode mode,
        TProp fallbackValue)
    {
        _target = target;
        _targetProperty = targetProperty;
        _convert = convert;
        _convertBack = convertBack;
        _fallbackValue = fallbackValue;
        _capabilities = BindingCapabilities.FromMode(mode);
        _observer = path.Attach(root);

        try
        {
            if (_capabilities.ObservesSourceChanges)
            {
                _observer.Changed += OnSourceChanged;
            }

            if (_capabilities.AcceptsTargetCommit)
            {
                _onTargetChanged = OnTargetChanged;
                target.AddPropertyBindingCallback(targetProperty.Id, _onTargetChanged);
            }

            if (_capabilities.ProvidesTargetValue)
            {
                OnSourceChanged();
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnSourceChanged()
    {
        if (_updating || _disposed)
        {
            return;
        }

        _updating = true;
        try
        {
            var value = _observer.IsAvailable
                ? _convert(_observer.CurrentValue)
                : _fallbackValue;

            if (!EqualityComparer<TProp>.Default.Equals(
                    _target.GetBindingValue(_targetProperty), value))
            {
                _target.UpdateBindingTarget(_targetProperty, value);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnTargetChanged()
    {
        if (_updating || _disposed)
        {
            return;
        }

        _updating = true;
        try
        {
            if (!_observer.IsAvailable || _convertBack == null)
            {
                _target.PropertyStore.ClearSource(_targetProperty.Id, ValueSource.Local);
                return;
            }

            _observer.Write(_convertBack(_target.GetBindingValue(_targetProperty)));

            // Refresh the Binding candidate before removing the transient Local candidate, so
            // source normalization is revealed as one final target value.
            var normalized = _convert(_observer.CurrentValue);
            _target.UpdateBindingTarget(_targetProperty, normalized);
            _target.PropertyStore.ClearSource(_targetProperty.Id, ValueSource.Local);
        }
        finally
        {
            _updating = false;
        }
    }

    public void UpdateTargetValue(object? value)
    {
        if (_updating || _disposed)
        {
            return;
        }

        _updating = true;
        try
        {
            _target.UpdateBindingTarget(_targetProperty, (TProp)value!);
        }
        finally
        {
            _updating = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_capabilities.ObservesSourceChanges)
        {
            _observer.Changed -= OnSourceChanged;
        }
        _observer.Dispose();

        if (_capabilities.AcceptsTargetCommit && _onTargetChanged != null)
        {
            _target.RemovePropertyBindingCallback(_targetProperty.Id, _onTargetChanged);
        }
    }
}
