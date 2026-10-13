using Kernel.Errors;

namespace Kernel.Exceptions;

/// <summary>
/// Base type for domain/application errors that map directly to an HTTP status code. The matching
/// exception handler turns these into <c>ProblemDetails</c> responses.
/// </summary>
public abstract class AppException : Exception
{
    /// <param name="statusCode">The HTTP status code this error maps to.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="code">The type's default code, answered unless the throw site sets a more specific one.</param>
    protected AppException(int statusCode, string message, ErrorCode code) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    /// <param name="statusCode">The HTTP status code this error maps to.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="code">The type's default code, answered unless the throw site sets a more specific one.</param>
    /// <param name="innerException">The underlying cause, preserved for logging.</param>
    protected AppException(int statusCode, string message, ErrorCode code, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    /// <summary>The HTTP status code this exception maps to.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// A stable URI identifying <em>this kind</em> of problem, written to <c>type</c> in the response.
    /// <c>null</c> leaves it to the default derived from the status code.
    /// </summary>
    /// <remarks>
    /// Only worth setting where a client must tell two problems apart that share a status code, and act
    /// differently on each — the status alone is too coarse and matching on the message text is not a
    /// contract. Everything else should leave this alone rather than mint a URI nobody reads.
    /// </remarks>
    public string? ProblemType { get; init; }

    /// <summary>
    /// The stable, machine-readable code identifying <em>this specific error</em>, written to <c>code</c> in
    /// the response. Each type has a generic default (<see cref="CommonErrors"/>); a throw site sets a module code
    /// where the reason is specific enough to earn its own localized message.
    /// </summary>
    /// <remarks>
    /// This is the handle a multilingual client translates on: the <c>detail</c> text is English and carries
    /// interpolated ids, so it is not a translation key. The code's <see cref="ErrorCode.Status"/> must equal
    /// <see cref="StatusCode"/>, and the endpoint must declare it with <c>ProducesErrors</c> (ADR-A017).
    /// </remarks>
    public ErrorCode Code
    {
        get;
        init => field = value.Status == StatusCode
            ? value
            : throw new ArgumentException(
                $"Code '{value.Value}' is answered with {value.Status}, but this exception maps to {StatusCode}.",
                nameof(value));
    }
}

/// <summary>The request was malformed in a way binding and validation cannot see. Maps to HTTP 400.</summary>
/// <param name="message">A human-readable description of what was wrong with the request.</param>
public sealed class BadRequestException(string message) : AppException(400, message, CommonErrors.Invalid);

/// <summary>A requested resource does not exist. Maps to HTTP 404.</summary>
/// <param name="message">A human-readable description of what was not found.</param>
public sealed class NotFoundException(string message) : AppException(404, message, CommonErrors.NotFound);

/// <summary>
/// The caller is authenticated but not allowed to perform this action (e.g. their role does not cover the
/// attempted operation). Maps to HTTP 403.
/// </summary>
/// <param name="message">A human-readable description of what was not allowed.</param>
public sealed class ForbiddenException(string message) : AppException(403, message, CommonErrors.Forbidden);

/// <summary>The request conflicts with current state (e.g. a duplicate key). Maps to HTTP 409.</summary>
/// <param name="message">A human-readable description of the conflict.</param>
public sealed class ConflictException(string message) : AppException(409, message, CommonErrors.Conflict);

/// <summary>
/// The request is fine but has to wait: a limit is reached, and <see cref="RetryAfter"/> says for how long. Maps to HTTP
/// 429 with a <c>Retry-After</c> header, and — when <see cref="RetryAt"/> is known — a <c>retryAt</c> timestamp in the
/// problem body, so a client can say "try again at 14:05" without doing arithmetic on a header it may not see.
/// </summary>
/// <remarks>
/// Takes its <see cref="AppException.Code"/> in the constructor: the same status covers limits with different remedies
/// (our own queue being full, an upstream throttling the account), and the code is how a client tells them apart.
/// The per-caller HTTP limit is answered by the rate limiter itself; this is for limits the application enforces.
/// </remarks>
public sealed class TooManyRequestsException : AppException
{
    /// <param name="message">A human-readable description of the limit.</param>
    /// <param name="code">The stable <c>module.reason</c> code.</param>
    /// <param name="retryAfter">How long to wait; written to <c>Retry-After</c>, rounded up to whole seconds.</param>
    /// <param name="retryAt">When the wait ends, if known; written to <c>retryAt</c>.</param>
    public TooManyRequestsException(string message, ErrorCode code, TimeSpan retryAfter, DateTimeOffset? retryAt = null)
        : base(429, message, code)
    {
        RetryAfter = retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;
        RetryAt = retryAt;
    }

    /// <summary>How long to wait before trying again.</summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>When the wait ends, if known.</summary>
    public DateTimeOffset? RetryAt { get; }
}

/// <summary>
/// An upstream dependency we don't control (e.g. Entra ID or the e-mail service) failed or rejected the request.
/// Maps to HTTP 502 so the caller knows the failure was downstream and the operation can be retried.
/// </summary>
/// <param name="message">A human-readable description of the failure.</param>
/// <param name="innerException">The underlying transport or provider error, preserved for logging.</param>
public sealed class ExternalServiceException(string message, Exception? innerException = null)
    : AppException(502, message, CommonErrors.UpstreamFailed, innerException);

/// <summary>
/// An upstream dependency could not be reached at all — the connection failed, or the call ran past its
/// timeout. Maps to HTTP 503 with a <c>Retry-After</c> header.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ExternalServiceException"/>, which means the dependency answered and the
/// answer was unusable. The difference matters to the caller: 502 says this request was rejected
/// downstream and repeating it verbatim will probably be rejected again, while 503 says nobody was home
/// and the very same request is worth retrying shortly. It also keeps an outage of something we don't
/// control out of the 5xx bucket that is meant to mean "this service has a bug".
/// </remarks>
/// <param name="message">A human-readable description of what could not be reached.</param>
/// <param name="innerException">The underlying transport or timeout error, preserved for logging.</param>
public sealed class ServiceUnavailableException(string message, Exception? innerException = null)
    : AppException(503, message, CommonErrors.UpstreamUnavailable, innerException)
{
    /// <summary>Seconds to put in <c>Retry-After</c>. Short: these outages are usually a restart.</summary>
    public int RetryAfterSeconds { get; init; } = 5;
}

/// <summary>
/// Raised by a handler when a command fails the cross-field rules DataAnnotations cannot express (the attributes
/// themselves are checked by the framework's validation, ADR-A008). Carries two member-keyed maps rendered as an
/// HTTP 400 validation problem: <see cref="Errors"/> (English messages, under <c>errors</c>) and the
/// index-aligned <see cref="ErrorCodes"/> (stable machine codes, under <c>errorCodes</c>) a multilingual
/// client translates on. Its own top-level code is <see cref="CommonErrors.ValidationFailed"/>.
/// </summary>
/// <param name="errors">Validation messages keyed by member name.</param>
/// <param name="errorCodes">Stable codes keyed by member name, index-aligned with <paramref name="errors"/>.</param>
public sealed class ValidationException(
    IReadOnlyDictionary<string, string[]> errors,
    IReadOnlyDictionary<string, FieldErrorCode[]> errorCodes)
    : AppException(400, "One or more validation errors occurred.", CommonErrors.ValidationFailed)
{
    /// <summary>The validation messages, keyed by the offending member name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    /// <summary>The stable codes, keyed by member name and index-aligned with <see cref="Errors"/>.</summary>
    public IReadOnlyDictionary<string, FieldErrorCode[]> ErrorCodes { get; } = errorCodes;

    /// <summary><see cref="ErrorCodes"/> as written to the response: the wire values.</summary>
    public IReadOnlyDictionary<string, string[]> ErrorCodeValues =>
        ErrorCodes.ToDictionary(e => e.Key, e => e.Value.Select(static c => c.Value).ToArray());

    /// <summary>Builds a single-field validation error — the common case for a handler's cross-field check.</summary>
    /// <param name="field">The offending member name.</param>
    /// <param name="code">The stable <c>module.reason</c> code the client translates on.</param>
    /// <param name="message">The English message.</param>
    public static ValidationException ForField(string field, FieldErrorCode code, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] },
            new Dictionary<string, FieldErrorCode[]> { [field] = [code] });
}