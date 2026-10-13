using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ApiService.Infrastructure;
using Kernel.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.ApiService;

/// <summary>
/// Every operation in the document promises <c>application/problem+json</c> for its errors, but the four
/// statuses the framework produces on its own — the authentication challenge (401), the authorization
/// failure (403), the routing miss (404) and the method mismatch (405) — used to come back with
/// <c>Content-Length: 0</c> and no <c>Content-Type</c> at all. A client that parses errors one way broke on
/// exactly the two it meets most often. These tests pin the <c>UseStatusCodePages</c> placement in
/// <c>Program.cs</c> that fills those in, and — just as important — that it leaves a response which already
/// has a body alone.
/// </summary>
public class ErrorResponseShapeTests
{
    private const string SchemeName = "Test";
    private const string UserHeader = "X-Test-User";

    /// <summary>Authenticates only when the request carries <see cref="UserHeader"/>, so the same host can produce both a 401 and a 403.</summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(UserHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            ClaimsPrincipal principal = new(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }

        /// <summary>Sets a challenge header the way JwtBearer does, so a test can prove the status-code body does not displace it.</summary>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.WWWAuthenticate = SchemeName;
            return base.HandleChallengeAsync(properties);
        }
    }

    /// <summary>
    /// A host carrying the pieces of <c>Program.cs</c> this behaviour depends on, in the same order: the
    /// ProblemDetails customisation, <c>UseStatusCodePages</c>, then authentication and authorization.
    /// </summary>
    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ProblemDetailsTelemetry.Customize(ctx);
            };
        });
        builder.Services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(SchemeName, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("never", policy => policy.RequireAssertion(static _ => false));

        WebApplication app = builder.Build();

        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/secure", static () => Results.Ok(new { ok = true })).RequireAuthorization();
        app.MapGet("/forbidden", static () => Results.Ok()).RequireAuthorization("never");
        app.MapGet("/own-problem", static () => Results.Problem(detail: "written by the endpoint", statusCode: 404));

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    [Fact]
    public void Trace_id_is_the_current_w3c_activity_trace_id()
    {
        const string expectedTraceId = "0123456789abcdef0123456789abcdef";
        using Activity activity = new("problem-details-test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.SetParentId($"00-{expectedTraceId}-0123456789abcdef-01");
        activity.Start();

        DefaultHttpContext http = new();
        http.Request.Path = "/api/v1/test";
        ProblemDetailsContext context = new()
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails(),
        };

        ProblemDetailsTelemetry.Customize(context);

        Assert.Equal(expectedTraceId, context.ProblemDetails.Extensions["traceId"]);
    }

    [Fact]
    public void A_code_the_writer_set_is_kept()
    {
        DefaultHttpContext http = new();
        ProblemDetailsContext context = new()
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Extensions = { ["code"] = CommonErrors.IdempotencyInProgress.Value },
            },
        };

        ProblemDetailsTelemetry.Customize(context);

        Assert.Equal(CommonErrors.IdempotencyInProgress.Value, context.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void A_framework_validation_problem_gets_the_validation_code()
    {
        ProblemDetailsContext context = new()
        {
            HttpContext = new DefaultHttpContext(),
            ProblemDetails = new HttpValidationProblemDetails { Status = StatusCodes.Status400BadRequest },
        };

        ProblemDetailsTelemetry.Customize(context);

        Assert.Equal(CommonErrors.ValidationFailed.Value, context.ProblemDetails.Extensions["code"]);
    }

    private static HttpRequestMessage SignedIn(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add(UserHeader, "test-user");
        return request;
    }

    /// <summary>
    /// Asserts the response is an RFC 7807 payload for <paramref name="expected"/>, carrying this service's customisation —
    /// including the status's default <c>code</c>, since every problem carries one (ADR-A017).
    /// </summary>
    private static async Task AssertProblemJsonAsync(HttpResponseMessage response, HttpStatusCode expected, string path, CancellationToken ct)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;

        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.Equal(path, problem.GetProperty("instance").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(CommonErrors.ForStatus((int)expected).Value, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unauthenticated_request_gets_a_problem_json_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplication app = await StartHostAsync(ct);
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        HttpResponseMessage response = await client.GetAsync("/secure", ct);

        await AssertProblemJsonAsync(response, HttpStatusCode.Unauthorized, "/secure", ct);
        // The challenge still tells the client how to authenticate — the body is added, not substituted.
        Assert.NotEmpty(response.Headers.WwwAuthenticate);

        await app.StopAsync(ct);
    }

    [Fact]
    public async Task A_denied_request_gets_a_problem_json_403()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplication app = await StartHostAsync(ct);
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        HttpResponseMessage response = await client.SendAsync(SignedIn(HttpMethod.Get, "/forbidden"), ct);

        await AssertProblemJsonAsync(response, HttpStatusCode.Forbidden, "/forbidden", ct);

        await app.StopAsync(ct);
    }

    [Fact]
    public async Task A_path_that_matches_no_route_gets_a_problem_json_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplication app = await StartHostAsync(ct);
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        HttpResponseMessage response = await client.SendAsync(SignedIn(HttpMethod.Get, "/no-such-thing"), ct);

        await AssertProblemJsonAsync(response, HttpStatusCode.NotFound, "/no-such-thing", ct);

        await app.StopAsync(ct);
    }

    [Fact]
    public async Task A_method_the_path_does_not_accept_gets_a_problem_json_405()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplication app = await StartHostAsync(ct);
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        HttpRequestMessage request = SignedIn(HttpMethod.Post, "/secure");
        request.Content = new StringContent("{}");
        HttpResponseMessage response = await client.SendAsync(request, ct);

        await AssertProblemJsonAsync(response, HttpStatusCode.MethodNotAllowed, "/secure", ct);

        await app.StopAsync(ct);
    }

    [Fact]
    public async Task A_response_that_already_has_a_body_is_left_alone()
    {
        // The other side of the contract. Every error this service raises deliberately — the exception
        // handler's ProblemDetails, the rate limiter's 429, the idempotency middleware's 409 — carries a detail
        // that explains it, and must not be flattened into a generic one.
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplication app = await StartHostAsync(ct);
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        HttpResponseMessage response = await client.SendAsync(SignedIn(HttpMethod.Get, "/own-problem"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        JsonElement problem = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal("written by the endpoint", problem.GetProperty("detail").GetString());

        await app.StopAsync(ct);
    }

    [Fact]
    public async Task A_successful_response_is_untouched()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplication app = await StartHostAsync(ct);
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        HttpResponseMessage response = await client.SendAsync(SignedIn(HttpMethod.Get, "/secure"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await app.StopAsync(ct);
    }
}
