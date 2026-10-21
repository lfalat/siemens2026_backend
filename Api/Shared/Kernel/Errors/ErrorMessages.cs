using Microsoft.AspNetCore.Http;

namespace Kernel.Errors;

/// <summary>
/// The Slovak messages the host and Kernel answer with, and the defaults every exception type uses when its throw site
/// names nothing more specific. Every problem's <c>detail</c> is a message the frontend shows as is (ADR-A017).
/// </summary>
public static class ErrorMessages
{
    // ── Request ─────────────────────────────────────────────────────────────

    /// <summary>A <c>ValidationException</c>'s <c>detail</c>, and the framework's own parameter validation.</summary>
    public const string ValidationFailed = "Niektoré údaje nie sú vyplnené správne.";

    /// <summary>The default for a <c>BadRequestException</c>.</summary>
    public const string Invalid = "Požiadavka nie je platná.";

    /// <summary>The body could not be read or a route or query value could not be parsed.</summary>
    public const string Malformed = "Požiadavka má nesprávny formát.";

    /// <summary>The default for a <c>NotFoundException</c> and the routing miss.</summary>
    public const string NotFound = "Hľadaný záznam neexistuje alebo k nemu nemáte prístup.";

    /// <summary>The route exists, but not for this HTTP method.</summary>
    public const string MethodNotAllowed = "Táto operácia nie je podporovaná.";

    /// <summary>The default for a <c>ConflictException</c>.</summary>
    public const string Conflict = "Akciu nie je možné vykonať v aktuálnom stave záznamu.";

    /// <summary>The body is larger than the server accepts.</summary>
    public const string PayloadTooLarge = "Odosielané údaje sú príliš veľké.";

    /// <summary>The body's media type is not one the endpoint reads.</summary>
    public const string UnsupportedMediaType = "Formát odosielaných údajov nie je podporovaný.";

    /// <summary>The per-caller rate limit was hit.</summary>
    public const string RateLimited = "Odoslali ste príliš veľa požiadaviek. Skúste to znova o chvíľu.";

    /// <summary>Another request changed the row first (optimistic concurrency).</summary>
    public const string ConcurrencyConflict = "Záznam medzitým zmenil niekto iný. Načítajte ho znova a zmenu zopakujte.";

    /// <summary>A text value holds a character PostgreSQL cannot store, most often a NUL.</summary>
    public const string UnstorableText = "Text obsahuje znak, ktorý nie je možné uložiť. Odstráňte ho a skúste to znova.";

    // ── Authentication and authorization ────────────────────────────────────

    /// <summary>No valid bearer token on a staff endpoint.</summary>
    public const string Unauthenticated = "Vaše prihlásenie vypršalo. Prihláste sa znova.";

    /// <summary>The default for a <c>ForbiddenException</c> and an authorization-policy failure.</summary>
    public const string Forbidden = "Na túto akciu nemáte oprávnenie.";

    // ── Upstream and server ─────────────────────────────────────────────────

    /// <summary>The default for an <c>ExternalServiceException</c>.</summary>
    public const string UpstreamFailed = "Externá služba požiadavku odmietla. Ak to pretrváva, kontaktujte správcu.";

    /// <summary>The default for a <c>ServiceUnavailableException</c>.</summary>
    public const string UpstreamUnavailable = "Externá služba je momentálne nedostupná. Skúste to znova o chvíľu.";

    /// <summary>An unhandled fault. The problem's <c>traceId</c> is what support needs.</summary>
    public const string ServerError = "Nastala neočakávaná chyba. Ak pretrváva, kontaktujte podporu a uveďte identifikátor chyby.";

    /// <summary>
    /// The message a problem written without a <c>detail</c> is given, by status: the framework's status pages,
    /// middleware outside the exception handler, a status no exception type covers.
    /// </summary>
    /// <param name="status">The response status.</param>
    public static string ForStatus(int status) => status switch
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
