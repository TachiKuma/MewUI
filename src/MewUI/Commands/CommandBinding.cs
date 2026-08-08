namespace Aprillz.MewUI;

/// <summary>
/// Handler pair (execute + can-execute query) registered for a command in a <see cref="CommandScope"/>.
/// One derived shape per public Bind overload so no closure allocation is needed.
/// </summary>
internal abstract class CommandBinding
{
    protected CommandBinding(Command command) => Command = command;

    public Command Command { get; }

    /// <summary>
    /// Queries current state; a binding without a can-execute predicate is always executable.
    /// </summary>
    public abstract bool CanExecute(in CommandContext context);

    public abstract ValueTask ExecuteAsync(in CommandContext context);
}

internal sealed class SimpleCommandBinding : CommandBinding
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public SimpleCommandBinding(Command command, Action execute, Func<bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke() ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute();
        return default;
    }
}

internal sealed class ContextCommandBinding : CommandBinding
{
    private readonly Action<CommandContext> _execute;
    private readonly Func<CommandContext, bool>? _canExecute;

    public ContextCommandBinding(Command command, Action<CommandContext> execute, Func<CommandContext, bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute(context);
        return default;
    }
}

internal sealed class AsyncContextCommandBinding : CommandBinding
{
    private readonly Func<CommandContext, ValueTask> _execute;
    private readonly Func<CommandContext, bool>? _canExecute;

    public AsyncContextCommandBinding(Command command, Func<CommandContext, ValueTask> execute, Func<CommandContext, bool>? canExecute)
        : base(command)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context) => _execute(context);
}

internal sealed class TargetCommandBinding<T> : CommandBinding
    where T : class
{
    private readonly T _target;
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public TargetCommandBinding(Command command, T target, Action<T> execute, Func<T, bool>? canExecute)
        : base(command)
    {
        _target = target;
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(_target) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute(_target);
        return default;
    }
}

internal sealed class TargetContextCommandBinding<T> : CommandBinding
    where T : class
{
    private readonly T _target;
    private readonly Action<T, CommandContext> _execute;
    private readonly Func<T, CommandContext, bool>? _canExecute;

    public TargetContextCommandBinding(Command command, T target, Action<T, CommandContext> execute, Func<T, CommandContext, bool>? canExecute)
        : base(command)
    {
        _target = target;
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(_target, context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context)
    {
        _execute(_target, context);
        return default;
    }
}

internal sealed class AsyncTargetContextCommandBinding<T> : CommandBinding
    where T : class
{
    private readonly T _target;
    private readonly Func<T, CommandContext, ValueTask> _execute;
    private readonly Func<T, CommandContext, bool>? _canExecute;

    public AsyncTargetContextCommandBinding(Command command, T target, Func<T, CommandContext, ValueTask> execute, Func<T, CommandContext, bool>? canExecute)
        : base(command)
    {
        _target = target;
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(in CommandContext context) => _canExecute?.Invoke(_target, context) ?? true;

    public override ValueTask ExecuteAsync(in CommandContext context) => _execute(_target, context);
}
