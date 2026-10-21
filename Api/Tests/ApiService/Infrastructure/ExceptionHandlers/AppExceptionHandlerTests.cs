using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Errors;
using Kernel.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.ApiService.Infrastructure.ExceptionHandlers;

/// <summary>
/// Pins what <see cref="AppExceptionHandler"/> writes for the Slovak-only frontend: the exception's message is the
/// <c>detail</c> the frontend shows as is, a <see cref="ValidationException"/>'s per-field messages surface under
/// <c>errors</c>, and a failure the framework or EF Core raises gets a Slovak default instead of its English text.
/// </summary>
public class AppExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IProblemDetailsService ProblemDetails() =>
        new ServiceCollection().AddLogging().AddProblemDetails()
            .BuildServiceProvider().GetRequiredService<IProblemDetailsService>();

    private static AppExceptionHandler Handler() => new(ProblemDetails());

    private static DefaultHttpContext Context()
    {
        DefaultHttpContext context = new();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/job-offers";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> WriteAsync(Exception exception) =>
        (await WriteToContextAsync(exception)).Problem;

    /// <summary>Handles <paramref name="exception"/> and returns the body <b>and</b> the context, for the cases that assert on headers.</summary>
    private static async Task<(JsonElement Problem, DefaultHttpContext Context)> WriteToContextAsync(
        Exception exception)
    {
        DefaultHttpContext context = Context();
        bool handled = await Handler().TryHandleAsync(context, exception, Ct);
        Assert.True(handled);

        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync(Ct);
        return (JsonDocument.Parse(body).RootElement, context);
    }

    [Fact]
    public async Task The_exceptions_message_is_the_detail()
    {
        JsonElement problem = await WriteAsync(new ConflictException("Ponuka je uzavretá."));

        Assert.Equal("Ponuka je uzavretá.", problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task An_exception_without_a_message_answers_its_types_default()
    {
        JsonElement problem = await WriteAsync(new NotFoundException());

        Assert.Equal(ErrorMessages.NotFound, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_malformed_request_answers_the_slovak_message_for_its_status_not_the_frameworks()
    {
        JsonElement problem = await WriteAsync(new BadHttpRequestException("Unexpected end of request content.", 400));

        Assert.Equal(ErrorMessages.Malformed, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_validation_error_surfaces_its_field_messages()
    {
        ValidationException validation = ValidationException.ForField(
            "SalaryMin", "Minimálny plat je vyšší ako maximálny.");

        JsonElement problem = await WriteAsync(validation);

        Assert.Equal(ErrorMessages.ValidationFailed, problem.GetProperty("detail").GetString());
        Assert.Equal("Minimálny plat je vyšší ako maximálny.",
            problem.GetProperty("errors").GetProperty("SalaryMin")[0].GetString());
        Assert.False(problem.TryGetProperty("errorCodes", out _));
    }

    [Fact]
    public async Task An_unreachable_dependency_tells_the_client_when_to_come_back()
    {
        (_, DefaultHttpContext context) =
            await WriteToContextAsync(new ServiceUnavailableException());

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("5", context.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_a_409_instead_of_a_500()
    {
        // e.g. a job offer edited by two recruiters at once: the loser's save sees a stale version.
        JsonElement problem = await WriteAsync(new DbUpdateConcurrencyException("The row was changed."));

        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());
        Assert.Equal(ErrorMessages.ConcurrencyConflict, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_limit_the_application_enforces_says_when_to_come_back_twice()
    {
        // The header in whole seconds, rounded up so a client never retries a moment too early, and the
        // timestamp in the body for a client that wants to say "try again at 14:05".
        DateTimeOffset retryAt = new(2026, 10, 13, 14, 5, 0, TimeSpan.Zero);
        (JsonElement problem, DefaultHttpContext context) = await WriteToContextAsync(new TooManyRequestsException(
            "Na kontrolu čaká priveľa súborov.", TimeSpan.FromSeconds(1.2), retryAt));

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("2", context.Response.Headers.RetryAfter);
        Assert.Equal(retryAt, problem.GetProperty("retryAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_problem_type_is_written_only_when_the_exception_names_one()
    {
        JsonElement named = await WriteAsync(
            new ConflictException("x") { ProblemType = "https://healthyhires.example/problems/offer-closed" });
        JsonElement unnamed = await WriteAsync(new ConflictException("x"));

        Assert.Equal("https://healthyhires.example/problems/offer-closed", named.GetProperty("type").GetString());
        Assert.NotEqual("https://healthyhires.example/problems/offer-closed", unnamed.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_genuine_fault_is_left_to_the_catch_all()
    {
        Assert.False(await Handler().TryHandleAsync(Context(), new InvalidOperationException("bug"), Ct));
    }
}
