using System.Buffers;
using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kernel.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kernel.Idempotency;

/// <summary>
/// Gives every covered <c>POST</c>/<c>PATCH</c> endpoint at-most-once semantics keyed by a client-supplied
/// <see cref="IdempotencyOptions.HeaderName"/> header (scoped per authenticated user and per endpoint):
/// the first request runs and its successful (<c>2xx</c>) response is captured; a repeat of the same key
/// replays that response verbatim, a still-running duplicate gets <c>409</c>, and the same key with a
/// different payload gets <c>422</c>. Endpoints marked <see cref="IdempotencyDisabled"/> pass straight
/// through. Errors mapped from thrown exceptions are produced upstream (by the exception handler) and are
/// not captured here, so they are never cached.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="options">Host-wide idempotency settings.</param>
/// <param name="problemDetails">Writes RFC 7807 responses for the middleware's own rejections.</param>
/// <param name="metrics">Counters for claim outcomes.</param>
/// <param name="logger">Logger for a failed write-back.</param>
/// <remarks>
/// The store arrives as an <see cref="InvokeAsync"/> parameter rather than a constructor dependency:
/// middleware is built once for the lifetime of the app, and a database-backed store is scoped. Injecting
/// it here would capture a single context for every request the process ever serves.
/// </remarks>
public sealed partial class IdempotencyMiddleware(
    RequestDelegate next,
    IOptions<IdempotencyOptions> options,
    IProblemDetailsService problemDetails,
    IdempotencyMetrics metrics,
    ILogger<IdempotencyMiddleware> logger)
{
    /// <summary>Read size for streaming the request body through the fingerprint hash.</summary>
    private const int BodyChunkSize = 8192;

    private readonly IdempotencyOptions _options = options.Value;

    /// <summary>Runs the idempotency handling for the current request.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="store">The backing idempotency store, resolved per request.</param>
    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
    {
        // RouteEndpoint, not Endpoint: when a path exists but does not accept this method, routing selects a
        // non-routable endpoint that answers 405. Treating that as a covered write meant a POST to a
        // GET-only path was rejected with 400 "Idempotency-Key is required" — an error about a header, for a
        // request whose method the resource never accepts, on an operation the document does not declare.
        if (!IsCoveredMethod(context.Request.Method) ||
            context.GetEndpoint() is not RouteEndpoint endpoint ||
            endpoint.Metadata.GetMetadata<IdempotencyDisabled>() is not null)
        {
            await next(context);
            return;
        }

        string? key = context.Request.Headers[_options.HeaderName];
        if (string.IsNullOrWhiteSpace(key))
        {
            if (!_options.RequireKey)
            {
                await next(context);
                return;
            }

            await WriteProblemAsync(context, CommonErrors.IdempotencyKeyMissing,
                $"A non-empty '{_options.HeaderName}' header is required for {context.Request.Method} requests.");
            return;
        }

        if (key.Length > _options.MaxKeyLength)
        {
            await WriteProblemAsync(context, CommonErrors.IdempotencyKeyTooLong,
                $"The '{_options.HeaderName}' header must be at most {_options.MaxKeyLength} characters.");
            return;
        }

        TimeSpan ttl = endpoint.Metadata.GetMetadata<IdempotencyTtl>()?.Ttl ?? _options.DefaultTtl;
        byte[] fingerprint = await ComputeFingerprintAsync(context);
        string cacheKey = BuildCacheKey(context, key);

        IdempotencyBeginResult begin = await store.BeginAsync(
            cacheKey, fingerprint, ttl, _options.ClaimLease, context.RequestAborted);

        metrics.RecordClaim(begin.Status);
        if (begin.Reclaimed)
            metrics.RecordReclaim();

        switch (begin.Status)
        {
            case IdempotencyBeginStatus.Completed:
                await ReplayAsync(context, begin.Record!);
                return;

            case IdempotencyBeginStatus.InProgress:
                await WriteProblemAsync(context, CommonErrors.IdempotencyInProgress,
                    $"A request with this '{_options.HeaderName}' is already being processed.");
                return;

            case IdempotencyBeginStatus.Mismatch:
                await WriteProblemAsync(context, CommonErrors.IdempotencyPayloadMismatch,
                    $"This '{_options.HeaderName}' was already used with a different request payload.");
                return;
        }

        // Started: we own execution. Buffer the response so we can both send it and remember it.
        await ExecuteAndCaptureAsync(context, endpoint, store, cacheKey);
    }

    /// <summary>Runs the endpoint with a buffered response body, records the outcome, then streams it to the client.</summary>
    private async Task ExecuteAndCaptureAsync(
        HttpContext context, RouteEndpoint endpoint, IIdempotencyStore store, string cacheKey)
    {
        // The route template, never the request path: it names the operation for an operator without
        // carrying the route values that identify a specific record.
        string route = endpoint.RoutePattern.RawText ?? "unmatched";
        Stream originalBody = context.Response.Body;
        using MemoryStream buffer = new();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        catch
        {
            // Restore the real body first. Everything after this point can fail, and the exception handler
            // upstream is about to write the error response — into whatever `Body` points at. Leaving it on
            // the buffer would aim that write at a stream this frame is about to dispose.
            context.Response.Body = originalBody;

            // Releasing the claim so a retry gets through is best-effort. A store that cannot be reached is
            // one of the likelier reasons the endpoint threw in the first place, and letting that second
            // failure escape would replace the exception that explains the request with one that does not.
            // The claim then simply waits out its lease.
            try
            {
                using CancellationTokenSource writeBack = new(_options.WriteBackTimeout);
                await store.AbandonAsync(cacheKey, writeBack.Token);
            }
            catch (Exception abandonFailure)
            {
                LogAbandonFailed(logger, context.Request.Method, route, abandonFailure);
            }

            throw; // the error response is written by the exception handler
        }

        context.Response.Body = originalBody;

        // Resolve the entry BEFORE flushing, not after. Writing to the client is the step most likely to
        // fail — a dropped connection is exactly the situation a retry follows — and an entry left in-flight
        // would answer that retry with 409 for the rest of the TTL, guarding work already committed.
        // Recording first makes the retry a replay instead.
        //
        // A store failure here must not destroy the response. The endpoint has already committed, so all
        // that is lost is the ability to replay it: the claim stays until its lease runs out and a later
        // retry re-executes. Rethrowing would report a 500 for work that succeeded, which is both wrong and
        // an invitation to retry it.
        try
        {
            using CancellationTokenSource writeBack = new(_options.WriteBackTimeout);
            int status = context.Response.StatusCode;
            bool succeeded = status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
            if (succeeded && buffer.Length <= _options.MaxCachedResponseBytes)
            {
                string? location = context.Response.Headers.Location.ToString() is { Length: > 0 } loc ? loc : null;
                await store.CompleteAsync(cacheKey, new IdempotencyRecord(status, context.Response.ContentType, location, buffer.ToArray()), writeBack.Token);
            }
            else
            {
                // Two reasons to abandon, and only one of them is uneventful. A non-success response is
                // abandoned by design: a later request may succeed, so nothing is lost by not pinning it.
                // An oversize one is a silent lapse — the endpoint committed its work, the response cannot
                // be replayed, and the next retry of that key will therefore run the whole thing again.
                // Nothing else in the system says so: the claim counter records a normal "started", and the
                // client sees an ordinary 2xx.
                if (succeeded)
                    LogResponseTooLarge(
                        logger, context.Request.Method, route, buffer.Length, _options.MaxCachedResponseBytes);

                await store.AbandonAsync(cacheKey, writeBack.Token);
            }
        }
        catch (Exception exception)
        {
            LogWriteBackFailed(logger, context.Request.Method, route, exception);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
    }

    // Both carry the exception object. These are store failures, not caller errors, so the message and stack
    // trace hold no caller data. They are also Error, meaning a human has to act on them, and a bare exception
    // type name gives that human nothing to act on. The route template and method say which operation lost
    // its replay guarantee.
    [LoggerMessage(EventId = 3200, EventName = "idempotency.writeback.failed", Level = LogLevel.Error,
        Message = "Idempotency write-back failed for {Method} {Route}; the response is being returned but cannot be replayed.")]
    private static partial void LogWriteBackFailed(ILogger logger, string method, string route, Exception exception);

    [LoggerMessage(EventId = 3202, EventName = "idempotency.response.too_large", Level = LogLevel.Warning,
        Message = "{Method} {Route} answered {ResponseBytes} bytes, over the {LimitBytes}-byte replay limit; "
                  + "the response was not stored, so a retry of this key re-runs the operation.")]
    private static partial void LogResponseTooLarge(
        ILogger logger, string method, string route, long responseBytes, int limitBytes);

    [LoggerMessage(EventId = 3201, EventName = "idempotency.claim.abandon_failed", Level = LogLevel.Error,
        Message = "Idempotency claim for {Method} {Route} could not be released after the endpoint failed; it will wait out its lease.")]
    private static partial void LogAbandonFailed(ILogger logger, string method, string route, Exception exception);

    /// <summary>
    /// Writes a previously-captured response back to the client, flagged as a replay. Only the status,
    /// <c>Content-Type</c> and <c>Location</c> are reproduced — the endpoints under this middleware set no
    /// other response headers, and capturing an open-ended header set would mean replaying framing headers
    /// that belong to the original connection.
    /// </summary>
    private async Task ReplayAsync(HttpContext context, IdempotencyRecord record)
    {
        context.Response.StatusCode = record.StatusCode;
        if (record.ContentType is not null)
            context.Response.ContentType = record.ContentType;
        if (record.Location is not null)
            context.Response.Headers.Location = record.Location;
        if (record.Body.Length > 0)
            context.Response.ContentLength = record.Body.Length; // a 204 carries neither body nor length
        context.Response.Headers[_options.ReplayHeaderName] = "true";
        await context.Response.Body.WriteAsync(record.Body, context.RequestAborted);
    }

    /// <summary>
    /// Only the non-idempotent writes are covered. Reads/deletes and <c>PUT</c> (idempotent by HTTP
    /// semantics — a repeat lands the same resource state) flow through untouched.
    /// </summary>
    /// <param name="method">The request method.</param>
    /// <returns><c>true</c> when a request with this method is subject to idempotency handling.</returns>
    /// <remarks>
    /// Public because the OpenAPI operation transformer that documents the
    /// <see cref="IdempotencyOptions.HeaderName"/> header has to describe exactly the operations this
    /// middleware acts on — reading the rule here is what keeps the two from drifting apart.
    /// </remarks>
    public static bool IsCoveredMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPatch(method);

    /// <summary>
    /// Scopes the key by acting user and by the target method + path so the same key on two different
    /// endpoints is handled independently rather than colliding into a payload mismatch. The parts are
    /// hashed rather than joined: the key is caller-supplied and <c>:</c> is legal in a path, so a plain
    /// join lets a caller shift the delimiter and collide two of their own entries.
    /// </summary>
    /// <remarks>
    /// The actor is the token's subject — the same identity the rate limiter partitions staff by. Anonymous
    /// applicants all share one actor, so their keys are scoped by method + path alone and must be random
    /// UUIDs that cannot be guessed (ADR-A016).
    /// </remarks>
    private static string BuildCacheKey(HttpContext context, string key)
    {
        // JwtBearer maps `sub` to NameIdentifier unless inbound claim mapping is turned off, so accept both.
        string actor = context.User.FindFirstValue("sub")
                       ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? "anonymous";

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDelimited(hash, actor);
        AppendDelimited(hash, context.Request.Method);
        AppendDelimited(hash, context.Request.Path.ToString());
        AppendDelimited(hash, key);
        return $"idem:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    /// <summary>
    /// Hashes the request identity (method, path, query) and body so a repeat of the key with a changed
    /// payload can be detected. Buffers the body and rewinds it so downstream model binding still reads it,
    /// but streams it through the hash rather than taking a second full copy in memory.
    /// </summary>
    private static async Task<byte[]> ComputeFingerprintAsync(HttpContext context)
    {
        context.Request.EnableBuffering();

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDelimited(hash, context.Request.Method);
        AppendDelimited(hash, context.Request.Path.ToString());
        AppendDelimited(hash, context.Request.QueryString.ToString());

        byte[] rented = ArrayPool<byte>.Shared.Rent(BodyChunkSize);
        try
        {
            int read;
            while ((read = await context.Request.Body.ReadAsync(rented, context.RequestAborted)) > 0)
                hash.AppendData(rented, 0, read); // last part hashed, so it needs no length prefix
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        context.Request.Body.Position = 0;
        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Appends one length-prefixed part to a hash, so that concatenating the parts is unambiguous and two
    /// different splits of the same characters cannot produce the same digest.
    /// </summary>
    private static void AppendDelimited(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    /// <summary>Writes an RFC 7807 problem response with its <c>code</c> (picking up the host's ProblemDetails customisation).</summary>
    private async Task WriteProblemAsync(HttpContext context, ErrorCode code, string detail)
    {
        context.Response.StatusCode = code.Status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = code.Status,
                Detail = detail,
                Extensions = { ["code"] = code.Value },
            },
        });
    }
}
