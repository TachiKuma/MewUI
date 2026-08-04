namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Service lookup of a text view, keyed by type. Ported code registers what it builds here and
/// finds it again by service type, which is how AvalonEdit hands a highlighter to code that only
/// holds a view.
/// </summary>
public sealed class ServiceContainer : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>Registers <paramref name="instance"/> under <paramref name="serviceType"/>.</summary>
    public void AddService(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        if (!serviceType.IsInstanceOfType(instance))
        {
            throw new ArgumentException($"{instance.GetType()} is not a {serviceType}.", nameof(instance));
        }
        _services[serviceType] = instance;
    }

    public void AddService<TService>(TService instance) where TService : class
        => AddService(typeof(TService), instance);

    /// <summary>Removes the registration, if any.</summary>
    public void RemoveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        _services.Remove(serviceType);
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.GetValueOrDefault(serviceType);
    }

    public TService? GetService<TService>() where TService : class
        => GetService(typeof(TService)) as TService;
}
