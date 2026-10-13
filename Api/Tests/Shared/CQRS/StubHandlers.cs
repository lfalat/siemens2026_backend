using Kernel.CQRS;

namespace Tests.Shared;

/// <summary>Test double for a command handler: returns a canned result or throws a canned exception.</summary>
internal sealed class StubHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult> where TCommand : ICommand
{
    private readonly Func<TCommand, TResult>? _result;
    private readonly Exception? _throws;

    public StubHandler(Func<TCommand, TResult> result) => _result = result;
    public StubHandler(Exception throws) => _throws = throws;

    public int Calls { get; private set; }

    public Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default)
    {
        Calls++;
        if (_throws is not null)
            throw _throws;
        return Task.FromResult(_result!(command));
    }
}

/// <summary>Test double for a void command handler: runs a canned action or throws a canned exception.</summary>
internal sealed class StubVoidHandler<TCommand> : ICommandHandler<TCommand> where TCommand : ICommand
{
    private readonly Action<TCommand>? _action;
    private readonly Exception? _throws;

    public StubVoidHandler(Action<TCommand>? action = null) => _action = action;
    public StubVoidHandler(Exception throws) => _throws = throws;

    public int Calls { get; private set; }

    public Task HandleAsync(TCommand command, CancellationToken ct = default)
    {
        Calls++;
        if (_throws is not null)
            throw _throws;
        _action?.Invoke(command);
        return Task.CompletedTask;
    }
}
