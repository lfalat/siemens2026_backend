using Kernel.CQRS;
using Kernel.Decorators;
using Kernel.Events;

namespace Tests.Shared.CQRS;

/// <summary>
/// Tests the Scrutor wiring in <see cref="CqrsRegistration"/>: the scan finds every handler shape, internal ones
/// included, and registers it scoped; the decorators wrap each command handler exactly once with validation inside
/// the event scope; queries are left undecorated. Handlers live in <see cref="Probe"/>, scanned on their own, so the
/// rest of the test assembly does not leak into the container.
/// </summary>
public class CqrsRegistrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Every_handler_shape_is_registered_scoped()
    {
        ServiceCollection services = new();
        services.AddCqrsHandlers(typeof(Probe).Assembly);

        Assert.Equal(ServiceLifetime.Scoped, Lifetime<ICommandHandler<Probe.Rename>>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<ICommandHandler<Probe.Create, Guid>>(services));
        Assert.Equal(ServiceLifetime.Scoped, Lifetime<IQueryHandler<Probe.Find, string>>(services));
    }

    [Fact]
    public async Task A_command_handler_is_validated_inside_its_event_scope()
    {
        await using ServiceProvider provider = Provider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ICommandHandler<Probe.Create, Guid> handler =
            scope.ServiceProvider.GetRequiredService<ICommandHandler<Probe.Create, Guid>>();

        // Outermost is the event scope, so everything below it — validation included — runs under the event name.
        Assert.IsType<EventScopeDecorator<Probe.Create, Guid>>(handler);

        await handler.HandleAsync(new Probe.Create { Title = "Backend developer" }, Ct);
        Assert.Equal(Probe.Create.EventName, provider.GetRequiredService<Probe.Recorder>().SeenEvent);

        await Assert.ThrowsAsync<Kernel.Exceptions.ValidationException>(
            () => handler.HandleAsync(new Probe.Create { Title = "" }, Ct));
    }

    [Fact]
    public async Task A_void_command_handler_is_decorated_too()
    {
        await using ServiceProvider provider = Provider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ICommandHandler<Probe.Rename> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<Probe.Rename>>();

        Assert.IsType<EventScopeDecorator<Probe.Rename>>(handler);
        await Assert.ThrowsAsync<Kernel.Exceptions.ValidationException>(
            () => handler.HandleAsync(new Probe.Rename { Id = Guid.Empty }, Ct));
    }

    [Fact]
    public async Task A_query_handler_is_not_decorated()
    {
        // Queries change nothing, so they are neither validated nor stamped with an event.
        await using ServiceProvider provider = Provider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Assert.IsType<Probe.FindHandler>(scope.ServiceProvider.GetRequiredService<IQueryHandler<Probe.Find, string>>());
    }

    [Fact]
    public void Decorating_with_no_handlers_registered_is_a_no_op()
    {
        // The host calls AddCqrsDecorators unconditionally, before any module exists.
        ServiceCollection services = new();

        Exception? failure = Record.Exception(() => services.AddCqrsDecorators());

        Assert.Null(failure);
        Assert.Empty(services);
    }

    [Fact]
    public void The_event_scope_is_gone_once_the_command_returns()
    {
        Assert.Null(EventScope.Current);
        using (EventScope.Begin("outer"))
        {
            using (EventScope.Begin("inner"))
                Assert.Equal("inner", EventScope.Current);
            Assert.Equal("outer", EventScope.Current);
        }
        Assert.Null(EventScope.Current);
    }

    private static ServiceLifetime Lifetime<TService>(ServiceCollection services) =>
        Assert.Single(services, static d => d.ServiceType == typeof(TService)).Lifetime;

    private static ServiceProvider Provider()
    {
        ServiceCollection services = new();
        services.AddSingleton<Probe.Recorder>();
        services.AddCqrsHandlers(typeof(Probe).Assembly);
        services.AddCqrsDecorators();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
