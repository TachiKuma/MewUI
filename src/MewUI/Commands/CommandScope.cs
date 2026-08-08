namespace Aprillz.MewUI;

/// <summary>
/// Semantic handler container mapping commands to execute/can-execute pairs for one context
/// (an element subtree, a window or the application).
/// </summary>
/// <remarks>
/// A scope holds at most one binding per command; rebinding requires an explicit
/// <see cref="Unbind"/> (or disposing the previous <see cref="CommandRegistration"/>).
/// <see cref="Parent"/> forms an explicit semantic chain that is independent of the visual tree.
/// Mutation is a UI-thread operation.
/// </remarks>
public sealed class CommandScope : IDisposable
{
    private Dictionary<Command, CommandBinding>? _bindings;
    private bool _disposed;

    public CommandScope(CommandScope? parent = null) => Parent = parent;

    /// <summary>
    /// Gets the explicit semantic parent scope consulted when this scope has no binding.
    /// </summary>
    public CommandScope? Parent { get; }

    /// <summary>
    /// Binds a parameterless handler.
    /// </summary>
    public CommandRegistration Bind(Command command, Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Register(new SimpleCommandBinding(command, execute, canExecute));
    }

    /// <summary>
    /// Binds a handler receiving the invocation context.
    /// </summary>
    public CommandRegistration Bind(Command command, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Register(new ContextCommandBinding(command, execute, canExecute));
    }

    /// <summary>
    /// Binds an asynchronous handler receiving the invocation context.
    /// </summary>
    public CommandRegistration Bind(Command command, Func<CommandContext, ValueTask> execute, Func<CommandContext, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Register(new AsyncContextCommandBinding(command, execute, canExecute));
    }

    /// <summary>
    /// Binds a handler invoked with the given target, enabling closure-free static lambdas.
    /// </summary>
    public CommandRegistration Bind<T>(Command command, T target, Action<T> execute, Func<T, bool>? canExecute = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(execute);
        return Register(new TargetCommandBinding<T>(command, target, execute, canExecute));
    }

    /// <summary>
    /// Binds a handler invoked with the given target and the invocation context.
    /// </summary>
    public CommandRegistration Bind<T>(Command command, T target, Action<T, CommandContext> execute, Func<T, CommandContext, bool>? canExecute = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(execute);
        return Register(new TargetContextCommandBinding<T>(command, target, execute, canExecute));
    }

    /// <summary>
    /// Binds an asynchronous handler invoked with the given target and the invocation context.
    /// </summary>
    public CommandRegistration Bind<T>(Command command, T target, Func<T, CommandContext, ValueTask> execute, Func<T, CommandContext, bool>? canExecute = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(execute);
        return Register(new AsyncTargetContextCommandBinding<T>(command, target, execute, canExecute));
    }

    /// <summary>
    /// Returns whether this scope (not its parents) has a binding for the command.
    /// </summary>
    public bool Contains(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _bindings?.ContainsKey(command) == true;
    }

    /// <summary>
    /// Removes this scope's binding for the command; returns false when none exists.
    /// </summary>
    public bool Unbind(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _bindings?.Remove(command) == true;
    }

    /// <summary>
    /// Removes all bindings from this scope.
    /// </summary>
    public void Clear() => _bindings?.Clear();

    /// <summary>
    /// Clears the scope and rejects further binding.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _bindings = null;
    }

    internal bool TryGetBinding(Command command, out CommandBinding binding)
    {
        if (_bindings != null && _bindings.TryGetValue(command, out var found))
        {
            binding = found;
            return true;
        }

        binding = null!;
        return false;
    }

    internal void RemoveRegistration(Command command, CommandBinding binding)
    {
        // Only the registration's own binding may be removed; a rebound command keeps its new handler.
        if (_bindings != null && _bindings.TryGetValue(command, out var current) && ReferenceEquals(current, binding))
        {
            _bindings.Remove(command);
        }
    }

    private CommandRegistration Register(CommandBinding binding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bindings = _bindings ??= new Dictionary<Command, CommandBinding>(capacity: 4);
        if (!bindings.TryAdd(binding.Command, binding))
        {
            throw new InvalidOperationException(
                $"Command '{binding.Command.Id}' is already bound in this scope. Unbind it first to replace the handler.");
        }

        return new CommandRegistration(this, binding);
    }
}
