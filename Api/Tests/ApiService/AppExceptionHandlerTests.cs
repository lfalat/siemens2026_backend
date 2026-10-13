using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
using Kernel.Errors;
using Kernel.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.ApiService;

/// <summary>
/// Pins the machine-readable additions <see cref="AppExceptionHandler"/> writes for a multilingual client:
/// an <see cref="AppException.Code"/> surfaces as a top-level <c>code</c>, and a
/// <see cref="ValidationException"/>'s per-field codes surface as an <c>errorCodes</c> map index-aligned with
/// <c>errors</c>. A code is written only when the exception carries one, so a plain 404 stays uncluttered.
/// </summary>
public class AppExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ErrorCode OfferClosed = new(StatusCodes.Status409Conflict, "job_offer.closed", "The offer is closed.");

    private static readonly ErrorCode ScanQueueFull = new(
        StatusCodes.Status429TooManyRequests, "file.scan_queue_full", "Too many uploads are waiting for a scan.");

    private static readonly FieldErrorCode SalaryRangeInverted = new("job_offer.salary_range_inverted", "Minimum above maximum.");

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
    public async Task A_conflict_with_a_code_surfaces_it_as_code()
    {
        JsonElement problem = await WriteAsync(
            new ConflictException("Applications are closed for this offer.") { Code = OfferClosed });

        Assert.Equal("job_offer.closed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_conflict_without_a_specific_code_answers_the_generic_one()
    {
        // Every problem carries a code (ADR-A017), so a client translates on `code` and never on the status.
        JsonElement problem = await WriteAsync(new ConflictException("Something conflicts."));

        Assert.Equal(CommonErrors.Conflict.Value, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_plain_not_found_answers_the_generic_code()
    {
        JsonElement problem = await WriteAsync(new NotFoundException("No such job offer."));

        Assert.Equal(CommonErrors.NotFound.Value, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_malformed_request_answers_the_code_for_its_status()
    {
        JsonElement problem = await WriteAsync(new BadHttpRequestException("Unexpected end of request content.", 400));

        Assert.Equal(CommonErrors.Malformed.Value, problem.GetProperty("code").GetString());
    }

    [Fact]
    public void A_code_answered_with_another_status_is_refused_where_it_is_set()
    {
        // A 409 exception carrying a 404 code would answer a status the document doesn't list the code under.
        Assert.Throws<ArgumentException>(() => new ConflictException("x") { Code = CommonErrors.NotFound });
    }

    [Fact]
    public async Task A_validation_error_surfaces_an_index_aligned_error_codes_map()
    {
        ValidationException validation = ValidationException.ForField(
            "SalaryMin", SalaryRangeInverted, "The minimum salary is above the maximum.");

        JsonElement problem = await WriteAsync(validation);

        Assert.Equal(CommonErrors.ValidationFailed.Value, problem.GetProperty("code").GetString());
        Assert.Equal("The minimum salary is above the maximum.",
            problem.GetProperty("errors").GetProperty("SalaryMin")[0].GetString());
        Assert.Equal("job_offer.salary_range_inverted",
            problem.GetProperty("errorCodes").GetProperty("SalaryMin")[0].GetString());
    }

    [Fact]
    public async Task An_unreachable_dependency_tells_the_client_when_to_come_back()
    {
        (_, DefaultHttpContext context) =
            await WriteToContextAsync(new ServiceUnavailableException("The e-mail service could not be reached."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("5", context.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_a_409_with_a_code_instead_of_a_500()
    {
        // e.g. a job offer edited by two recruiters at once: the loser's save sees a stale version.
        JsonElement problem = await WriteAsync(new DbUpdateConcurrencyException("The row was changed."));

        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());
        Assert.Equal(CommonErrors.ConcurrencyConflict.Value, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_limit_the_application_enforces_says_when_to_come_back_twice()
    {
        // The header in whole seconds, rounded up so a client never retries a moment too early, and the
        // timestamp in the body for a client that wants to say "try again at 14:05".
        DateTimeOffset retryAt = new(2026, 10, 13, 14, 5, 0, TimeSpan.Zero);
        (JsonElement problem, DefaultHttpContext context) = await WriteToContextAsync(new TooManyRequestsException(
            "Too many uploads are waiting for a scan.", ScanQueueFull, TimeSpan.FromSeconds(1.2), retryAt));

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("2", context.Response.Headers.RetryAfter);
        Assert.Equal("file.scan_queue_full", problem.GetProperty("code").GetString());
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
