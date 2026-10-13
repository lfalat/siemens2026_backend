using Microsoft.AspNetCore.Http;

namespace Kernel.Errors;

/// <summary>
/// The codes the host and Kernel produce, and the defaults every exception type answers with when its throw site
/// names nothing more specific. Every problem response carries a <c>code</c> (ADR-A017).
/// </summary>
public static class CommonErrors
{
    // ── Request ─────────────────────────────────────────────────────────────

    /// <summary>A <c>ValidationException</c>'s top-level code, and the framework's own parameter validation.</summary>
    public static readonly ErrorCode ValidationFailed = new(StatusCodes.Status400BadRequest,
        "request.validation_failed", "One or more fields failed validation; see `errors` (and `errorCodes`, when present) for each field.");

    /// <summary>The default for a <c>BadRequestException</c>.</summary>
    public static readonly ErrorCode Invalid = new(StatusCodes.Status400BadRequest,
        "request.invalid", "The request is invalid.");

    /// <summary>The body could not be read or a route or query value could not be parsed.</summary>
    public static readonly ErrorCode Malformed = new(StatusCodes.Status400BadRequest,
        "request.malformed", "The request body or a route or query value could not be parsed.");

    /// <summary>The default for a <c>NotFoundException</c> and the routing miss.</summary>
    public static readonly ErrorCode NotFound = new(StatusCodes.Status404NotFound,
        "request.not_found", "The resource does not exist or is not visible to the caller.");

    /// <summary>The route exists, but not for this HTTP method.</summary>
    public static readonly ErrorCode MethodNotAllowed = new(StatusCodes.Status405MethodNotAllowed,
        "request.method_not_allowed", "The route does not accept this HTTP method.");

    /// <summary>The default for a <c>ConflictException</c>.</summary>
    public static readonly ErrorCode Conflict = new(StatusCodes.Status409Conflict,
        "request.conflict", "The request conflicts with the current state of the resource.");

    /// <summary>The body is larger than the server accepts.</summary>
    public static readonly ErrorCode PayloadTooLarge = new(StatusCodes.Status413PayloadTooLarge,
        "request.payload_too_large", "The request body is too large.");

    /// <summary>The body's media type is not one the endpoint reads.</summary>
    public static readonly ErrorCode UnsupportedMediaType = new(StatusCodes.Status415UnsupportedMediaType,
        "request.unsupported_media_type", "The request body's media type is not supported.");

    /// <summary>The per-caller rate limit was hit.</summary>
    public static readonly ErrorCode RateLimited = new(StatusCodes.Status429TooManyRequests,
        "request.rate_limited", "Too many requests; retry after the interval in the `Retry-After` header.");

    /// <summary>Another request changed the row first (optimistic concurrency).</summary>
    public static readonly ErrorCode ConcurrencyConflict = new(StatusCodes.Status409Conflict,
        "concurrency.conflict", "Another request changed the resource first; reload it and try again.");

    // ── Authentication and authorization ────────────────────────────────────

    /// <summary>No valid bearer token on a staff endpoint.</summary>
    public static readonly ErrorCode Unauthenticated = new(StatusCodes.Status401Unauthorized,
        "auth.unauthenticated", "The bearer token is missing, expired or invalid; sign in again.");

    /// <summary>The default for a <c>ForbiddenException</c> and an authorization-policy failure.</summary>
    public static readonly ErrorCode Forbidden = new(StatusCodes.Status403Forbidden,
        "auth.forbidden", "The caller's role does not allow this action.");

    // ── Idempotency ─────────────────────────────────────────────────────────

    /// <summary>The <c>Idempotency-Key</c> header is required and was not sent.</summary>
    public static readonly ErrorCode IdempotencyKeyMissing = new(StatusCodes.Status400BadRequest,
        "idempotency.key_missing", "The `Idempotency-Key` header is missing or empty.");

    /// <summary>The <c>Idempotency-Key</c> header is longer than allowed.</summary>
    public static readonly ErrorCode IdempotencyKeyTooLong = new(StatusCodes.Status400BadRequest,
        "idempotency.key_too_long", "The `Idempotency-Key` header is too long.");

    /// <summary>A request with the same key is still running.</summary>
    public static readonly ErrorCode IdempotencyInProgress = new(StatusCodes.Status409Conflict,
        "idempotency.in_progress", "A request with this `Idempotency-Key` is still being processed.");

    /// <summary>The key was used before with a different body.</summary>
    public static readonly ErrorCode IdempotencyPayloadMismatch = new(StatusCodes.Status422UnprocessableEntity,
        "idempotency.payload_mismatch", "This `Idempotency-Key` was already used with a different request payload.");

    // ── Upstream and server ─────────────────────────────────────────────────

    /// <summary>The default for an <c>ExternalServiceException</c>.</summary>
    public static readonly ErrorCode UpstreamFailed = new(StatusCodes.Status502BadGateway,
        "upstream.failed", "A service this request depends on rejected it or answered unusably.");

    /// <summary>The default for a <c>ServiceUnavailableException</c>.</summary>
    public static readonly ErrorCode UpstreamUnavailable = new(StatusCodes.Status503ServiceUnavailable,
        "upstream.unavailable", "A service this request depends on could not be reached; retry after `Retry-After`.");

    /// <summary>An unhandled fault.</summary>
    public static readonly ErrorCode ServerError = new(StatusCodes.Status500InternalServerError,
        "server.error", "An unexpected server error; report the `traceId`.");

    /// <summary>
    /// What any endpoint of the versioned API can answer before or around its own handler, signed in or not: an
    /// unparseable body or route value, the per-caller rate limit. Declared once, on the group. A staff group adds
    /// <see cref="Unauthenticated"/> when it requires sign-in; anonymous applicant endpoints never answer it.
    /// </summary>
    public static IReadOnlyList<ErrorCode> AnyEndpoint => [Malformed, RateLimited];

    /// <summary>
    /// The code a problem written without one is given, by status: the framework's status pages, middleware outside
    /// the exception handler, a status no exception type covers.
    /// </summary>
    /// <param name="status">The response status.</param>
    public static ErrorCode ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => Malformed,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => RateLimited,
        StatusCodes.Status502BadGateway => UpstreamFailed,
        StatusCodes.Status503ServiceUnavailable => UpstreamUnavailable,
        >= 500 => ServerError,
        _ => Invalid,
    };
}
