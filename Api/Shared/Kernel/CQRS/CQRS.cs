namespace Kernel.CQRS;

/// <summary>
/// A command: a request that mutates state. Every command names the feature event it performs, and the
/// compiler requires it — e.g. <c>job_offer_published</c>. The name is made ambient for the handler's duration
/// (see <see cref="Decorators.EventScopeDecorator{TCommand}"/>), so the audit trail and in-process messaging
/// (ADR-A001) can record which feature caused a change without the handler passing it along.
/// </summary>
public interface ICommand
{
    /// <summary>The feature event name: lower snake_case, no module prefix.</summary>
    static abstract string EventName { get; }
}

/// <summary>
/// Handles a command that mutates state but returns no value. Implementations are discovered and
/// registered per module by <see cref="CqrsRegistration.AddCqrsHandlers"/> and wrapped with the
/// validation decorator.
/// </summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>
/// Handles a command that mutates state and returns a result.
/// </summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
/// <typeparam name="TResult">The result produced by the command.</typeparam>
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand
{
    /// <summary>Executes the command and returns its result.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The result of the command.</returns>
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>
/// Handles a read-only query and returns a result. Queries are not validated by the
/// command validation decorator.
/// </summary>
/// <typeparam name="TQuery">The query type to handle.</typeparam>
/// <typeparam name="TResult">The result produced by the query.</typeparam>
public interface IQueryHandler<TQuery, TResult>
{
    /// <summary>Executes the query and returns its result.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The result of the query.</returns>
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}
