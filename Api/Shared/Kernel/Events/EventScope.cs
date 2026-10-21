namespace Kernel.Events;

/// <summary>
/// The ambient name of the feature event currently running — the command's <see cref="CQRS.ICommand.EventName"/>,
/// opened by <see cref="Decorators.EventScopeDecorator{TCommand}"/>. Whatever records a change — an audit
/// entry, a message to another module — reads it here instead of having every handler pass it along. Flows with
/// the async call chain and restores on dispose, so a command that runs another command sees the inner name only
/// for the inner command's duration.
/// </summary>
public static class EventScope
{
    /// <summary>
    /// The event recorded for a save that ran outside any command or worker (seed, test fixture, an
    /// uncovered code path). It is monitored rather than rejected: the change is still recorded.
    /// </summary>
    public const string Unnamed = "unnamed";

    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The event name of the innermost active scope, or <c>null</c> outside any scope.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>Makes <paramref name="eventName"/> the ambient event until the returned scope is disposed.</summary>
    /// <param name="eventName">The feature event name (lower snake_case, no module prefix).</param>
    /// <returns>A scope that restores the previous event on dispose.</returns>
    public static IDisposable Begin(string eventName)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        string? previous = Ambient.Value;
        Ambient.Value = eventName;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
