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

        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Initialize()
    {
        if (_capabilities.ProvidesTargetValue)
        {
            OnSourceChanged();
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

    public void UpdateTargetValue(object? value)
    {
        if (_disposed)
        {
            return;
        }

        _target.UpdateBindingTarget(_targetProperty, (TProp)value!);
    }

    public object? CommitTargetValue(object? value)
    {
        if (_disposed || !_observer.IsAvailable || _convertBack == null)
        {
            return value;
        }

        _updating = true;
        try
        {
            _observer.Write(_convertBack((TProp)value!));
            return _convert(_observer.CurrentValue);
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

    }
}
