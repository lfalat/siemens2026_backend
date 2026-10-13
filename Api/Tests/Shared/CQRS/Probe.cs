using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Events;
using Kernel.Validation;

namespace Tests.Shared.CQRS;

/// <summary>
/// One handler of each CQRS shape, internal like a module's own, for <see cref="CqrsRegistrationTests"/> to find
/// by scanning.
/// </summary>
internal static class Probe
{
    /// <summary>
    /// Records the event name a command handler saw — what the event-scope decorator is for. A singleton the handler
    /// writes to, because a value set on an <see cref="AsyncLocal{T}"/> inside an async call never flows back out.
    /// </summary>
    public sealed class Recorder
    {
        public string? SeenEvent { get; set; }
    }

    public sealed record Create : ICommand
    {
        public static string EventName => "job_offer_created";

        [Required]
        public string Title { get; init; } = string.Empty;
    }

    public sealed record Rename : ICommand
    {
        public static string EventName => "job_offer_renamed";

        [Required, NotEmpty]
        public Guid Id { get; init; }
    }

    public sealed record Find(Guid Id);

    internal sealed class CreateHandler(Recorder recorder) : ICommandHandler<Create, Guid>
    {
        public Task<Guid> HandleAsync(Create command, CancellationToken ct = default)
        {
            recorder.SeenEvent = EventScope.Current;
            return Task.FromResult(Guid.CreateVersion7());
        }
    }

    internal sealed class RenameHandler : ICommandHandler<Rename>
    {
        public Task HandleAsync(Rename command, CancellationToken ct = default) => Task.CompletedTask;
    }

    internal sealed class FindHandler : IQueryHandler<Find, string>
    {
        public Task<string> HandleAsync(Find query, CancellationToken ct = default) => Task.FromResult("found");
    }
}
