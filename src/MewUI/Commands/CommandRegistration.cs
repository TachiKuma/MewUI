namespace Aprillz.MewUI;

/// <summary>
/// Lifetime token for a single <see cref="CommandScope"/> binding; disposing removes that binding.
/// </summary>
public sealed class CommandRegistration : IDisposable
{
    private CommandScope? _scope;
    private readonly CommandBinding _binding;

    internal CommandRegistration(CommandScope scope, CommandBinding binding)
    {
        _scope = scope;
        _binding = binding;
    }

    /// <summary>
    /// Gets the bound command.
    /// </summary>
    public Command Command => _binding.Command;

    public void Dispose()
    {
        var scope = _scope;
        _scope = null;
        scope?.RemoveRegistration(_binding.Command, _binding);
    }
}
