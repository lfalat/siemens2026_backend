using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Kernel.Errors;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Shared.Idempotency;

/// <summary>
/// Tests the Shared/Kernel <see cref="IdempotencyMiddleware"/> request handling against a controllable
/// <see cref="IIdempotencyStore"/>: it requires a well-formed key on covered writes (POST/PATCH), leaves
/// reads, PUT, and opted-out endpoints alone, replays a completed response, rejects an in-flight duplicate
/// (409) and a payload mismatch (422), captures only successful (2xx) responses within the size cap, and
/// honours a per-endpoint TTL override. Also pins the two failure paths that decide whether a retry can
/// ever succeed: a throwing endpoint releases the claim, and a response that fails to reach the client is
/// still recorded.
/// </summary>
public class IdempotencyMiddlewareTests
{
    private const string Actor = "subject-aa";

    /// <summary>Store test double that returns a scripted <see cref="BeginAsync"/> result and records the follow-up call.</summary>
    private sealed class FakeStore : IIdempotencyStore
    {
        public IdempotencyBeginResult Next { get; set; } = new(IdempotencyBeginStatus.Started, null);
        public string? Key { get; private set; }
        public TimeSpan BeganTtl { get; private set; }
        public IdempotencyRecord? Completed { get; private set; }
        public bool Abandoned { get; private set; }

        public TimeSpan BeganLease { get; private set; }

        public Task<IdempotencyBeginResult> BeginAsync(
            string key, byte[] fingerprint, TimeSpan ttl, TimeSpan lease, CancellationToken ct = default)
        {
            Key = key;
            BeganTtl = ttl;
            BeganLease = lease;
            return Task.FromResult(Next);
        }

        public Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken ct = default)
        {
            Completed = record;
            return Task.CompletedTask;
        }

        public Task AbandonAsync(string key, CancellationToken ct = default)
        {
            Abandoned = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>Counts invocations and writes a scripted status + body when the pipeline reaches "the endpoint".</summary>
    private sealed class SpyNext(int status, string body)
    {
        public int Calls { get; private set; }

        public async Task Invoke(HttpContext context)
        {
            Calls++;
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body);
        }
    }

    /// <summary>Stands in for a client that disappears mid-response: accepts the swap, fails the flush.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("client is gone");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("client is gone"));
    }

    private static IdempotencyMetrics TestMetrics() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());

    private static IProblemDetailsService ProblemDetails() =>
        new ServiceCollection().AddLogging().AddProblemDetails()
            .BuildServiceProvider().GetRequiredService<IProblemDetailsService>();

    private static (DefaultHttpContext ctx, MemoryStream response) BuildContext(
        string method, string? key, byte[]? body = null, params object[] endpointMetadata) =>
        BuildContext(method, key, "/api/v1/job-offers", body, endpointMetadata);

    private static (DefaultHttpContext ctx, MemoryStream response) BuildContext(
        string method, string? key, string path, byte[]? body, params object[] endpointMetadata)
    {
        DefaultHttpContext ctx = new();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Body = new MemoryStream(body ?? []);
        ctx.Request.ContentLength = body?.Length ?? 0;
        if (key is not null)
            ctx.Request.Headers["Idempotency-Key"] = key;

        MemoryStream response = new();
        ctx.Response.Body = response;

        // A RouteEndpoint, because that is what routing selects for a request the resource accepts — and
        // the middleware distinguishes it from the plain Endpoint that answers 405 (see the 405 test below).
        ctx.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask, RoutePatternFactory.Parse(path), order: 0,
            new EndpointMetadataCollection(endpointMetadata), "test"));
        return (ctx, response);
    }

    /// <summary>A signed-in caller identified the way a token identifies them, by its subject claim.</summary>
    private static ClaimsPrincipal SignedIn(string subject, string claimType = "sub") =>
        new(new ClaimsIdentity([new Claim(claimType, subject)], authenticationType: "Bearer"));

    private static async Task InvokeAsync(
        IIdempotencyStore store, HttpContext ctx, RequestDelegate next,
        IdempotencyOptions? options = null, string? actor = Actor)
    {
        if (actor is not null)
            ctx.User = SignedIn(actor);

        IdempotencyMiddleware middleware = new(
            next, Options.Create(options ?? new IdempotencyOptions()), ProblemDetails(), TestMetrics(),
            NullLogger<IdempotencyMiddleware>.Instance);
        await middleware.InvokeAsync(ctx, store);
    }

    private static string ReadBody(MemoryStream response)
    {
        response.Position = 0;
        return new StreamReader(response, Encoding.UTF8).ReadToEnd();
    }

    /// <summary>The <c>code</c> of the problem the middleware wrote — each rejection has its own.</summary>
    private static string? CodeOf(MemoryStream response) =>
        JsonDocument.Parse(ReadBody(response)).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Missing_key_on_covered_write_is_rejected_with_400()
    {
        FakeStore store = new();
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: null);

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        Assert.Equal(0, next.Calls);
        Assert.Equal(CommonErrors.IdempotencyKeyMissing.Value, CodeOf(response));
    }

    [Fact]
    public async Task Key_longer_than_the_limit_is_rejected_with_400()
    {
        FakeStore store = new();
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: new string('k', 129));

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
        Assert.Equal(0, next.Calls);
        Assert.Equal(CommonErrors.IdempotencyKeyTooLong.Value, CodeOf(response));
        Assert.Null(store.Key); // rejected before the key ever reaches the store
    }

    [Fact]
    public async Task Get_request_passes_through_untouched()
    {
        FakeStore store = new();
        SpyNext next = new(200, "read");
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("GET", key: null);

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(1, next.Calls);
        Assert.Null(store.Completed);
    }

    [Fact]
    public async Task Put_passes_through_untouched() // idempotent by HTTP semantics — not covered
    {
        FakeStore store = new();
        SpyNext next = new(200, "replaced");
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("PUT", key: null);

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(1, next.Calls); // no 400 despite the missing key
        Assert.Null(store.Completed);
    }

    [Fact]
    public async Task Disabled_endpoint_passes_through_without_requiring_a_key()
    {
        FakeStore store = new();
        SpyNext next = new(200, "ok");
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: null, body: null, IdempotencyDisabled.Instance);

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(1, next.Calls);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Null(store.Completed);
    }

    [Fact]
    public async Task First_request_executes_and_caches_a_successful_response()
    {
        FakeStore store = new(); // Begin defaults to Started
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc", body: "{\"n\":1}"u8.ToArray());

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(1, next.Calls);
        Assert.Equal("created", ReadBody(response)); // streamed to the client
        Assert.NotNull(store.Completed);
        Assert.Equal(201, store.Completed.StatusCode);
        Assert.Equal("created", Encoding.UTF8.GetString(store.Completed.Body));
        Assert.False(store.Abandoned);
    }

    [Fact]
    public async Task Non_success_response_is_not_cached()
    {
        FakeStore store = new();
        SpyNext next = new(409, "conflict");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(1, next.Calls);
        Assert.Equal("conflict", ReadBody(response)); // still streamed
        Assert.Null(store.Completed);
        Assert.True(store.Abandoned);
    }

    [Fact]
    public async Task Response_beyond_the_size_cap_is_sent_but_not_cached()
    {
        FakeStore store = new();
        SpyNext next = new(200, new string('x', 200));
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke, new IdempotencyOptions { MaxCachedResponseBytes = 64 });

        Assert.Equal(new string('x', 200), ReadBody(response)); // the client still gets it in full
        Assert.Null(store.Completed);
        Assert.True(store.Abandoned); // and the key is released rather than left in-flight
    }

    [Fact]
    public async Task Throwing_endpoint_abandons_the_claim_and_rethrows()
    {
        // The exception handler upstream writes the error response; leaving the claim in place would answer
        // every retry with 409 even though nothing was committed.
        FakeStore store = new();
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(store, ctx, _ => throw new InvalidOperationException("boom")));

        Assert.True(store.Abandoned);
        Assert.Null(store.Completed);
    }

    [Fact]
    public async Task Response_that_fails_to_reach_the_client_is_still_recorded()
    {
        // A dropped connection is exactly the situation the client retries after, and the work is already
        // committed by this point. The record has to exist so that retry replays instead of getting a 409
        // for the rest of the TTL.
        FakeStore store = new();
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc");
        ctx.Response.Body = new FailingStream();

        await Assert.ThrowsAsync<IOException>(() => InvokeAsync(store, ctx, next.Invoke));

        Assert.Equal(1, next.Calls);
        Assert.NotNull(store.Completed);
        Assert.Equal("created", Encoding.UTF8.GetString(store.Completed.Body));
        Assert.False(store.Abandoned);
    }

    [Fact]
    public async Task Completed_key_replays_without_running_the_endpoint()
    {
        IdempotencyRecord record = new(201, "application/json", "/api/v1/job-offers/1", "cached-body"u8.ToArray());
        FakeStore store = new() { Next = new IdempotencyBeginResult(IdempotencyBeginStatus.Completed, record) };
        SpyNext next = new(500, "should-not-run");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("PATCH", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(0, next.Calls);
        Assert.Equal(201, ctx.Response.StatusCode);
        Assert.Equal("cached-body", ReadBody(response));
        Assert.Equal("/api/v1/job-offers/1", ctx.Response.Headers.Location.ToString());
        Assert.Equal("true", ctx.Response.Headers["Idempotency-Replayed"].ToString());
        Assert.Equal(record.Body.Length, ctx.Response.ContentLength);
    }

    [Fact]
    public async Task Replay_of_an_empty_body_sets_no_content_length()
    {
        // A 204 carries neither a body nor a Content-Length; stamping a zero would be a malformed response.
        IdempotencyRecord record = new(204, null, null, []);
        FakeStore store = new() { Next = new IdempotencyBeginResult(IdempotencyBeginStatus.Completed, record) };
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, _ => Task.CompletedTask);

        Assert.Equal(204, ctx.Response.StatusCode);
        Assert.Null(ctx.Response.ContentLength);
    }

    [Fact]
    public async Task In_flight_duplicate_is_rejected_with_409()
    {
        FakeStore store = new() { Next = new IdempotencyBeginResult(IdempotencyBeginStatus.InProgress, null) };
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
        Assert.Equal(0, next.Calls);
        Assert.Equal(CommonErrors.IdempotencyInProgress.Value, CodeOf(response));
    }

    [Fact]
    public async Task Key_reused_with_different_payload_is_rejected_with_422()
    {
        FakeStore store = new() { Next = new IdempotencyBeginResult(IdempotencyBeginStatus.Mismatch, null) };
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ctx.Response.StatusCode);
        Assert.Equal(0, next.Calls);
        Assert.Equal(CommonErrors.IdempotencyPayloadMismatch.Value, CodeOf(response));
    }

    [Fact]
    public async Task Endpoint_ttl_override_is_used_instead_of_the_default()
    {
        FakeStore store = new();
        SpyNext next = new(200, "ok");
        TimeSpan overrideTtl = TimeSpan.FromMinutes(5);
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc", body: null, new IdempotencyTtl(overrideTtl));

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(overrideTtl, store.BeganTtl);
    }

    [Fact]
    public async Task Missing_key_passes_through_when_key_is_not_required()
    {
        FakeStore store = new();
        SpyNext next = new(200, "ok");
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: null);

        await InvokeAsync(store, ctx, next.Invoke, new IdempotencyOptions { RequireKey = false });

        Assert.Equal(1, next.Calls);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Request_body_is_rewound_for_model_binding()
    {
        // The fingerprint streams the body through the hash rather than copying it; downstream binding must
        // still see the whole payload.
        FakeStore store = new();
        string? seen = null;
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc", body: "{\"n\":1}"u8.ToArray());

        await InvokeAsync(store, ctx, async context =>
        {
            seen = await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync();
            context.Response.StatusCode = 200;
        });

        Assert.Equal("{\"n\":1}", seen);
    }

    [Fact]
    public async Task The_same_key_scopes_to_a_different_entry_per_user()
    {
        FakeStore first = new();
        FakeStore second = new();
        (DefaultHttpContext ctxA, MemoryStream _) = BuildContext("POST", key: "abc");
        (DefaultHttpContext ctxB, MemoryStream __) = BuildContext("POST", key: "abc");

        await InvokeAsync(first, ctxA, _ => Task.CompletedTask, actor: Actor);
        await InvokeAsync(second, ctxB, _ => Task.CompletedTask, actor: "subject-bb");

        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public async Task A_mapped_name_identifier_scopes_like_the_sub_claim_it_came_from()
    {
        // JwtBearer maps `sub` to NameIdentifier unless inbound claim mapping is off. Whichever the host ends up
        // with, one user's key has to land on one entry, or a retry would miss the claim it is retrying.
        FakeStore viaSub = new();
        FakeStore viaNameIdentifier = new();
        (DefaultHttpContext ctxA, MemoryStream _) = BuildContext("POST", key: "abc");
        (DefaultHttpContext ctxB, MemoryStream __) = BuildContext("POST", key: "abc");
        ctxB.User = SignedIn(Actor, ClaimTypes.NameIdentifier);

        await InvokeAsync(viaSub, ctxA, _ => Task.CompletedTask);
        await InvokeAsync(viaNameIdentifier, ctxB, _ => Task.CompletedTask, actor: null);

        Assert.Equal(viaSub.Key, viaNameIdentifier.Key);
    }

    [Fact]
    public async Task Anonymous_callers_are_scoped_apart_from_signed_in_users()
    {
        // Applicants never sign in. Their keys share one scope per endpoint — which is why they must be random
        // UUIDs (ADR-A016) — but never the scope of a staff user who happens to send the same key.
        FakeStore anonymous = new();
        FakeStore signedIn = new();
        (DefaultHttpContext ctxA, MemoryStream _) = BuildContext("POST", key: "abc");
        (DefaultHttpContext ctxB, MemoryStream __) = BuildContext("POST", key: "abc");

        await InvokeAsync(anonymous, ctxA, _ => Task.CompletedTask, actor: null);
        await InvokeAsync(signedIn, ctxB, _ => Task.CompletedTask);

        Assert.NotNull(anonymous.Key);
        Assert.NotEqual(anonymous.Key, signedIn.Key);
    }

    [Fact]
    public async Task A_key_cannot_be_crafted_to_collide_with_another_path()
    {
        // The parts are length-prefixed before hashing. Joining them with ':' would make ("/a", "b:c") and
        // ("/a:b", "c") the same entry, and ':' is legal in a path.
        FakeStore first = new();
        FakeStore second = new();
        (DefaultHttpContext ctxA, MemoryStream _) = BuildContext("POST", "b:c", "/a", body: null);
        (DefaultHttpContext ctxB, MemoryStream __) = BuildContext("POST", "c", "/a:b", body: null);

        await InvokeAsync(first, ctxA, _ => Task.CompletedTask);
        await InvokeAsync(second, ctxB, _ => Task.CompletedTask);

        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public async Task A_method_the_path_does_not_accept_passes_through_to_the_405()
    {
        // Routing selects a non-routable endpoint for a method mismatch. Claiming it as a covered write made
        // every POST/PATCH to a read-only path answer 400 "Idempotency-Key is required" instead of 405 —
        // which a Schemathesis run (ADR-009) reports as an undeclared status on every such path.
        FakeStore store = new();
        SpyNext next = new(StatusCodes.Status405MethodNotAllowed, string.Empty);

        DefaultHttpContext ctx = new();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/v1/job-offers";
        ctx.Request.Body = new MemoryStream([]);
        ctx.Response.Body = new MemoryStream();
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask, EndpointMetadataCollection.Empty, "405 HTTP Method Not Supported"));

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(1, next.Calls);
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, ctx.Response.StatusCode);
        Assert.Null(store.Key); // never claimed an entry for a request the resource cannot serve
    }

    [Fact]
    public async Task A_real_method_mismatch_reaches_the_405_through_the_middleware()
    {
        // Closes the loop the fake endpoint above leaves open: that routing really does hand this middleware
        // something other than a RouteEndpoint when the method does not match, in a host built the usual way.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddProblemDetails();
        builder.Services.AddIdempotency();

        WebApplication app = builder.Build();
        app.UseIdempotency();
        app.MapGet("/job-offers", static () => Results.Ok());

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(TestContext.Current.CancellationToken);

        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };
        HttpResponseMessage response = await client.PostAsync(
            "/job-offers", new StringContent("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void MapQuery_maps_a_post_route_that_is_opted_out()
    {
        // The helper's whole contract: still a POST on the wire, but invisible to this middleware.
        WebApplication app = WebApplication.CreateSlimBuilder().Build();
        app.MapQuery("/things/search", static () => Results.Ok());

        Endpoint endpoint = Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(static d => d.Endpoints));

        Assert.Equal(["POST"], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IdempotencyDisabled>());
    }

    /// <summary>Store whose write-back fails, as it would when the database goes away mid-request.</summary>
    private sealed class FailingCompleteStore : IIdempotencyStore
    {
        public Task<IdempotencyBeginResult> BeginAsync(
            string key, byte[] fingerprint, TimeSpan ttl, TimeSpan lease, CancellationToken ct = default) =>
            Task.FromResult(new IdempotencyBeginResult(IdempotencyBeginStatus.Started, null));

        public Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken ct = default) =>
            throw new InvalidOperationException("store unavailable");

        public Task AbandonAsync(string key, CancellationToken ct = default) =>
            throw new InvalidOperationException("store unavailable");
    }

    [Fact]
    public async Task A_store_failure_after_the_endpoint_succeeded_still_returns_its_response()
    {
        // The endpoint has already committed by this point. Losing the ability to remember the response is
        // a lost replay, not a failed write — turning it into a 500 would tell the client its work failed
        // when it did not, and invite a retry of something already applied.
        FailingCompleteStore store = new();
        SpyNext next = new(201, "created");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(201, ctx.Response.StatusCode);
        Assert.Equal("created", ReadBody(response));
    }

    [Fact]
    public async Task A_store_failure_after_a_non_success_response_still_returns_it()
    {
        FailingCompleteStore store = new();
        SpyNext next = new(400, "bad request");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke);

        Assert.Equal(400, ctx.Response.StatusCode);
        Assert.Equal("bad request", ReadBody(response));
    }

    [Fact]
    public async Task An_exception_releases_the_claim_even_though_the_endpoint_may_have_committed()
    {
        // Documents a known limitation rather than a desired behaviour. The middleware cannot tell an
        // exception thrown *by* the handler (nothing committed — releasing is right) from one thrown after
        // it returned (committed — releasing lets a retry repeat the work). It releases either way,
        // because holding the claim would delay every legitimate retry by a whole lease instead.
        FakeStore store = new();
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc");

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(store, ctx, async _ =>
        {
            await Task.Yield(); // stands in for a handler that has already committed
            throw new InvalidOperationException("thrown after the work landed");
        }));

        Assert.True(store.Abandoned);
        Assert.Null(store.Completed);
    }

    [Fact]
    public async Task A_failure_to_release_the_claim_does_not_replace_the_endpoints_exception()
    {
        // The two failures are correlated: a store that cannot be reached is one of the likelier reasons
        // the endpoint threw. Letting the store's exception win would swap the one that explains the
        // request for one that explains nothing, and every such request would be diagnosed as an
        // idempotency fault.
        FailingCompleteStore store = new();
        (DefaultHttpContext ctx, MemoryStream _) = BuildContext("POST", key: "abc");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(store, ctx, _ => throw new InvalidOperationException("the endpoint failed")));

        Assert.Equal("the endpoint failed", thrown.Message);
    }

    [Fact]
    public async Task A_failing_release_still_leaves_the_response_writable()
    {
        // Whatever handles the exception upstream writes the error response into Response.Body. If the
        // middleware unwinds without restoring it, that write lands on the buffer this frame just disposed.
        FailingCompleteStore store = new();
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(store, ctx, _ => throw new InvalidOperationException("the endpoint failed")));

        Assert.Same(response, ctx.Response.Body);
        await ctx.Response.WriteAsync("problem+json", TestContext.Current.CancellationToken);
        Assert.Equal("problem+json", ReadBody(response));
    }

    [Theory]
    [InlineData(201)]
    [InlineData(400)]
    public async Task A_stalled_write_back_is_cancelled_without_losing_the_endpoint_response(int status)
    {
        StalledStore store = new();
        SpyNext next = new(status, "endpoint response");
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");

        await InvokeAsync(store, ctx, next.Invoke,
            new IdempotencyOptions { WriteBackTimeout = TimeSpan.FromMilliseconds(50) })
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(store.WriteToken.IsCancellationRequested);
        Assert.Equal(status, ctx.Response.StatusCode);
        Assert.Equal("endpoint response", ReadBody(response));
    }

    [Fact]
    public async Task A_stalled_release_does_not_hold_or_replace_the_endpoint_exception()
    {
        StalledStore store = new();
        (DefaultHttpContext ctx, MemoryStream response) = BuildContext("POST", key: "abc");
        InvalidOperationException original = new("endpoint failed");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(store, ctx, _ => throw original,
                new IdempotencyOptions { WriteBackTimeout = TimeSpan.FromMilliseconds(50) })
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(original, thrown);
        Assert.True(store.WriteToken.IsCancellationRequested);
        Assert.Same(response, ctx.Response.Body);
    }

    private sealed class StalledStore : IIdempotencyStore
    {
        public CancellationToken WriteToken { get; private set; }
        public Task<IdempotencyBeginResult> BeginAsync(string key, byte[] fingerprint, TimeSpan ttl, TimeSpan lease,
            CancellationToken ct = default) => Task.FromResult(new IdempotencyBeginResult(IdempotencyBeginStatus.Started, null));
        public Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken ct = default) => Stall(ct);
        public Task AbandonAsync(string key, CancellationToken ct = default) => Stall(ct);
        private Task Stall(CancellationToken ct)
        {
            WriteToken = ct;
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }
}
