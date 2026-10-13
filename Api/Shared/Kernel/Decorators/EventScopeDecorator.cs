using Kernel.CQRS;
using Kernel.Events;

namespace Kernel.Decorators;

/// <summary>
/// Opens an <see cref="EventScope"/> with the command's <see cref="ICommand.EventName"/> around a void command
/// handler, so anything recorded while it runs — an audit entry, a message to another module — can say which
/// feature caused it. Handler code does not change.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <param name="inner">The handler to run inside the scope.</param>
public sealed class EventScopeDecorator<TCommand>(
    ICommandHandler<TCommand> inner) : ICommandHandler<TCommand> where TCommand : ICommand
{
    /// <summary>Runs the inner handler inside the command's event scope.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task HandleAsync(TCommand command, CancellationToken ct = default)
    {
        using (EventScope.Begin(TCommand.EventName))
            await inner.HandleAsync(command, ct);
    }
}

/// <summary>
/// Opens an <see cref="EventScope"/> with the command's <see cref="ICommand.EventName"/> around a
/// result-returning command handler. See <see cref="EventScopeDecorator{TCommand}"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result produced by the command.</typeparam>
/// <param name="inner">The handler to run inside the scope.</param>
public sealed class EventScopeDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner) : ICommandHandler<TCommand, TResult> where TCommand : ICommand
{
    /// <summary>Runs the inner handler inside the command's event scope.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The result produced by the inner handler.</returns>
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default)
    {
        using (EventScope.Begin(TCommand.EventName))
            return await inner.HandleAsync(command, ct);
    }
}
