namespace Kernel.Errors;

/// <summary>
/// A stable, machine-readable error code — the key a bilingual client translates on, since a problem's
/// <c>detail</c> is English and interpolates ids (ADR-A017, <c>docs/api/errors/errors.md</c>).
/// </summary>
/// <remarks>
/// Two kinds, kept apart by type so one cannot be passed where the other belongs: an <see cref="ErrorCode"/> is a
/// problem's top-level <c>code</c>; a <see cref="FieldErrorCode"/> is a value under <c>errorCodes[field]</c> of a
/// validation problem. Each module declares its codes in one <c>&lt;Module&gt;Errors</c> class; codes shared across
/// modules or produced by the host are in <see cref="CommonErrors"/> and <see cref="ValidationRuleErrors"/>.
/// </remarks>
public interface IErrorCode
{
    /// <summary>The wire value, dotted <c>area.reason</c> (e.g. <c>document.not_draft</c>). Part of the API contract.</summary>
    string Value { get; }

    /// <summary>What the code means, for the OpenAPI document and the translators. English, one sentence.</summary>
    string Description { get; }
}

/// <summary>A problem's top-level <c>code</c>, answered with <see cref="Status"/>.</summary>
/// <param name="Status">The HTTP status the code is answered with.</param>
/// <param name="Value">The wire value.</param>
/// <param name="Description">What the code means.</param>
public sealed record ErrorCode(int Status, string Value, string Description) : IErrorCode
{
    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>A per-field code under <c>errorCodes[field]</c> of a <c>400</c> validation problem.</summary>
/// <param name="Value">The wire value.</param>
/// <param name="Description">What the code means.</param>
public sealed record FieldErrorCode(string Value, string Description) : IErrorCode
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
