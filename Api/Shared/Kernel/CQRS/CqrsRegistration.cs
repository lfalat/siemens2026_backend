using System.Reflection;
using Kernel.Decorators;
using Microsoft.Extensions.DependencyInjection;

namespace Kernel.CQRS;

/// <summary>
/// Helpers that wire CQRS handlers and their cross-cutting decorators into the DI container.
/// </summary>
public static class CqrsRegistration
{
    /// <summary>
    /// Scans <paramref name="assembly"/> and registers every <see cref="ICommandHandler{TCommand}"/>,
    /// <see cref="ICommandHandler{TCommand,TResult}"/>, and <see cref="IQueryHandler{TQuery,TResult}"/>
    /// implementation (including non-public ones) with scoped lifetime. Call once per module.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="assembly">The module assembly to scan for handlers.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCqrsHandlers(this IServiceCollection services, Assembly assembly)
    {
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());
        return services;
    }

    /// <summary>
    /// Decorates every registered command handler with <see cref="ValidationDecorator{TCommand}"/>
    /// /<see cref="ValidationDecorator{TCommand,TResult}"/> so commands are validated before
    /// they run, and outermost with <see cref="EventScopeDecorator{TCommand}"/>
    /// /<see cref="EventScopeDecorator{TCommand,TResult}"/> so every change the handler saves carries
    /// the command's <see cref="ICommand.EventName"/>. Must be called once in <c>Program.cs</c> <em>after</em> all module registrations so each
    /// handler is wrapped exactly once. A no-op when no matching handler is registered.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    // Called ONCE in Program.cs, AFTER all AddXModule() calls.
    public static IServiceCollection AddCqrsDecorators(this IServiceCollection services)
    {
        // TryDecorate is a no-op if nothing of that shape is registered.
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(EventScopeDecorator<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(EventScopeDecorator<,>));
        return services;
    }
}
