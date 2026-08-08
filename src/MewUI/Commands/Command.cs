namespace Aprillz.MewUI;

/// <summary>
/// Identifies a semantic operation (e.g. "file.save"). A command owns neither its handler nor its
/// shortcut; handlers live in <see cref="CommandScope"/> and gestures in <see cref="InputMap"/>.
/// </summary>
/// <remarks>
/// Runtime lookup uses reference identity: two <see cref="Command"/> instances with the same
/// <see cref="Id"/> are distinct commands. <see cref="Id"/> is a stable textual identity for
/// diagnostics, logging and persistence.
/// </remarks>
public sealed class Command
{
    public Command(string id, string? text = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Text = text;
    }

    /// <summary>
    /// Gets the stable textual identity of this command.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the default presentation label, or null when presentation supplies its own text.
    /// </summary>
    public string? Text { get; }

    public override string ToString() => Id;
}
