using Microsoft.Extensions.Compliance.Classification;

namespace Kernel.Telemetry;

/// <summary>Data classifications used by HealthyHires telemetry.</summary>
public static class HealthyHiresDataClassification
{
    private const string TaxonomyName = "HealthyHires";

    /// <summary>
    /// Data that identifies, or can be linked to, a person: an applicant's name, e-mail or phone number, a staff
    /// member's account.
    /// </summary>
    public static readonly DataClassification PersonalData = new(TaxonomyName, nameof(PersonalData));

    /// <summary>
    /// Credentials and caller-controlled content that must not be exported: tokens, SAS URLs, cover-letter text,
    /// uploaded file names.
    /// </summary>
    public static readonly DataClassification SensitiveData = new(TaxonomyName, nameof(SensitiveData));
}

/// <summary>Marks a structured logging argument as personal data.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PersonalDataAttribute() : DataClassificationAttribute(HealthyHiresDataClassification.PersonalData);

/// <summary>Marks a structured logging argument as sensitive data.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class SensitiveDataAttribute() : DataClassificationAttribute(HealthyHiresDataClassification.SensitiveData);
