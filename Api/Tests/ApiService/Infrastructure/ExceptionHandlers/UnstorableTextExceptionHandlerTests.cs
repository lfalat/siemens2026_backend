using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Errors;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Tests.Shared;

namespace Tests.ApiService.Infrastructure.ExceptionHandlers;

/// <summary>
/// A NUL byte is legal in JSON and legal in a .NET string, and PostgreSQL's <c>text</c> cannot store it —
/// so every text field the API persists would turn a caller's bad data into a <c>500</c> — and Schemathesis
/// sends exactly such values (ADR-009). These tests pin both halves: that PostgreSQL really answers
/// <c>22021</c> for it, and that the handler turns exactly that into a <c>400</c> and nothing else.
/// </summary>
public class UnstorableTextExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IProblemDetailsService ProblemDetails() =>
        new ServiceCollection().AddLogging().AddProblemDetails()
            .BuildServiceProvider().GetRequiredService<IProblemDetailsService>();

    private sealed class RecordingLogger : ILogger<UnstorableTextExceptionHandler>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    private static UnstorableTextExceptionHandler Handler(RecordingLogger? logger = null) =>
        new(ProblemDetails(), logger ?? new RecordingLogger());

    private static DefaultHttpContext Context()
    {
        DefaultHttpContext context = new();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/job-offers";
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task A_nul_byte_really_is_rejected_by_postgres_as_22021()
    {
        // The premise the handler is built on, checked against the real schema rather than assumed. Any text
        // column will do; the idempotency key is the one the schema has today.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        db.Entries.Add(new IdempotencyEntry
        {
            Key = "idem:\0",
            Fingerprint = [1],
            ClaimOwner = Guid.CreateVersion7(),
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });

        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        PostgresException postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal("22021", postgres.SqlState);
    }

    [Fact]
    public async Task It_is_answered_with_400_rather_than_escaping_as_a_500()
    {
        DefaultHttpContext context = Context();

        bool handled = await Handler().TryHandleAsync(
            context, new DbUpdateException("save failed", new PostgresException(
                "invalid byte sequence for encoding \"UTF8\": 0x00", "ERROR", "ERROR", "22021")), Ct);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task The_cause_is_found_however_deeply_it_is_wrapped()
    {
        // EF wraps it once; a retrying execution strategy (ADR-A011) can wrap it again.
        DefaultHttpContext context = Context();

        bool handled = await Handler().TryHandleAsync(
            context,
            new InvalidOperationException("retries exhausted",
                new DbUpdateException("save failed", new PostgresException(
                    "invalid byte sequence", "ERROR", "ERROR", "22021"))),
            Ct);

        Assert.True(handled);
    }

    [Theory]
    [InlineData("23505")] // unique_violation — a conflict the handler must not disguise as bad input
    [InlineData("23503")] // foreign_key_violation
    [InlineData("57014")] // query_canceled
    public async Task Another_postgres_failure_is_left_to_the_catch_all(string sqlState)
    {
        DefaultHttpContext context = Context();

        bool handled = await Handler().TryHandleAsync(
            context, new DbUpdateException("save failed", new PostgresException(
                "something else", "ERROR", "ERROR", sqlState)), Ct);

        Assert.False(handled);
    }

    [Fact]
    public async Task An_unrelated_exception_is_left_alone()
    {
        DefaultHttpContext context = Context();

        Assert.False(await Handler().TryHandleAsync(context, new InvalidOperationException("nope"), Ct));
    }

    [Fact]
    public async Task The_response_does_not_echo_the_offending_value_back()
    {
        // The value is caller-supplied and, by definition here, carries bytes that belong neither in a
        // response nor in a log.
        DefaultHttpContext context = Context();

        await Handler().TryHandleAsync(
            context, new DbUpdateException("save failed", new PostgresException(
                "invalid byte sequence for encoding \"UTF8\": 0x00", "ERROR", "ERROR", "22021")), Ct);

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync(Ct);

        Assert.DoesNotContain("0x00", body, StringComparison.Ordinal);
        Assert.Equal(ErrorMessages.UnstorableText,
            JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_expected_failure_does_not_export_the_exception_message()
    {
        const string marker = "caller-value-in-database-error";
        RecordingLogger logger = new();
        DefaultHttpContext context = Context();

        await Handler(logger).TryHandleAsync(
            context, new DbUpdateException("save failed", new PostgresException(
                marker, "ERROR", "ERROR", "22021")), Ct);

        Assert.Single(logger.Messages);
        Assert.DoesNotContain(marker, logger.Messages[0], StringComparison.Ordinal);
        Assert.DoesNotContain(context.Request.Path.Value!, logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("unmatched", logger.Messages[0], StringComparison.Ordinal);
        Assert.All(logger.Exceptions, Assert.Null);
    }
}
