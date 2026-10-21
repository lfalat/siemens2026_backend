using Kernel.Errors;

namespace Kernel.Exceptions;

/// <summary>
/// Base type for domain/application errors that map directly to an HTTP status code. The matching
/// exception handler turns these into <c>ProblemDetails</c> responses.
/// </summary>
/// <remarks>
/// The message is the problem's <c>detail</c>, which the frontend shows the user as is (ADR-A017): write it in Slovak,
/// for the person using the app, and leave out what they cannot act on. Each type defaults to a message from
/// <see cref="ErrorMessages"/>; a throw site passes its own where the reason is specific.
/// </remarks>
public abstract class AppException : Exception
{
    /// <param name="statusCode">The HTTP status code this error maps to.</param>
    /// <param name="message">The Slovak message shown to the user.</param>
    /// <param name="innerException">The underlying cause, preserved for logging.</param>
    protected AppException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
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
}

/// <summary>The request was malformed in a way binding and validation cannot see. Maps to HTTP 400.</summary>
/// <param name="message">The Slovak message shown to the user.</param>
public sealed class BadRequestException(string message = ErrorMessages.Invalid) : AppException(400, message);

/// <summary>A requested resource does not exist. Maps to HTTP 404.</summary>
/// <param name="message">The Slovak message shown to the user.</param>
public sealed class NotFoundException(string message = ErrorMessages.NotFound) : AppException(404, message);

/// <summary>
/// The caller is authenticated but not allowed to perform this action (e.g. their role does not cover the
/// attempted operation). Maps to HTTP 403.
/// </summary>
/// <param name="message">The Slovak message shown to the user.</param>
public sealed class ForbiddenException(string message = ErrorMessages.Forbidden) : AppException(403, message);

/// <summary>The request conflicts with current state (e.g. a duplicate key). Maps to HTTP 409.</summary>
/// <param name="message">The Slovak message shown to the user.</param>
public sealed class ConflictException(string message = ErrorMessages.Conflict) : AppException(409, message);

/// <summary>
/// The request is fine but has to wait: a limit is reached, and <see cref="RetryAfter"/> says for how long. Maps to HTTP
/// 429 with a <c>Retry-After</c> header, and — when <see cref="RetryAt"/> is known — a <c>retryAt</c> timestamp in the
/// problem body, so a client can say "try again at 14:05" without doing arithmetic on a header it may not see.
/// </summary>
/// <remarks>
/// Has no default message: the same status covers limits with different remedies (our own queue being full, an upstream
/// throttling the account), and the message is what tells the user which one they met. The per-caller HTTP limit is
/// answered by the rate limiter itself; this is for limits the application enforces.
/// </remarks>
public sealed class TooManyRequestsException : AppException
{
    /// <param name="message">The Slovak message shown to the user: which limit, and what to do.</param>
    /// <param name="retryAfter">How long to wait; written to <c>Retry-After</c>, rounded up to whole seconds.</param>
    /// <param name="retryAt">When the wait ends, if known; written to <c>retryAt</c>.</param>
    public TooManyRequestsException(string message, TimeSpan retryAfter, DateTimeOffset? retryAt = null)
        : base(429, message)
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
/// <param name="message">The Slovak message shown to the user.</param>
/// <param name="innerException">The underlying transport or provider error, preserved for logging.</param>
public sealed class ExternalServiceException(string message = ErrorMessages.UpstreamFailed, Exception? innerException = null)
    : AppException(502, message, innerException);

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
/// <param name="message">The Slovak message shown to the user.</param>
/// <param name="innerException">The underlying transport or timeout error, preserved for logging.</param>
public sealed class ServiceUnavailableException(string message = ErrorMessages.UpstreamUnavailable, Exception? innerException = null)
    : AppException(503, message, innerException)
{
    /// <summary>Seconds to put in <c>Retry-After</c>. Short: these outages are usually a restart.</summary>
    public int RetryAfterSeconds { get; init; } = 5;
}

/// <summary>
/// Raised by a handler when a command fails the cross-field rules DataAnnotations cannot express, and by the validation
/// decorator when an attribute fails (ADR-A008). Rendered as an HTTP 400 validation problem whose <c>errors</c> map
/// holds the Slovak messages per member, which the frontend shows next to each field.
/// </summary>
/// <param name="errors">Slovak validation messages keyed by member name.</param>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException(400, ErrorMessages.ValidationFailed)
{
    /// <summary>The validation messages, keyed by the offending member name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    /// <summary>Builds a single-field validation error — the common case for a handler's cross-field check.</summary>
    /// <param name="field">The offending member name.</param>
    /// <param name="message">The Slovak message shown next to the field.</param>
    public static ValidationException ForField(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
