using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tests.ApiService;

/// <summary>
/// The catch-all: a genuine fault is logged with the route template — not the path, which carries ids and applicant
/// values — and answered with a bare <c>500</c> that carries <c>server.error</c> and nothing from the exception.
/// </summary>
public class UnhandledExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingLogger : ILogger<UnhandledExceptionHandler>
    {
        public List<(int Id, string Message, Exception? Exception)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add((eventId.Id, formatter(state, exception), exception));
    }

    [Fact]
    public async Task A_fault_is_a_500_with_the_server_error_code_and_no_detail_from_the_exception()
    {
        const string secret = "connection string with a password in it";
        RecordingLogger logger = new();
        UnhandledExceptionHandler handler = new(
            new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider()
                .GetRequiredService<IProblemDetailsService>(),
            logger);
        DefaultHttpContext context = new();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/job-offers/0193f6a2-0000-7000-8000-000000000001/applications";
        context.Response.Body = new MemoryStream();
        InvalidOperationException fault = new(secret);

        Assert.True(await handler.TryHandleAsync(context, fault, Ct));

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync(Ct);
        JsonElement problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(CommonErrors.ServerError.Value, problem.GetProperty("code").GetString());
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);

        (int id, string message, Exception? logged) = Assert.Single(logger.Events);
        Assert.Equal(1000, id);
        Assert.Same(fault, logged); // the operator needs the stack trace; the caller must not get it
        Assert.DoesNotContain(context.Request.Path.Value!, message, StringComparison.Ordinal);
    }
}
