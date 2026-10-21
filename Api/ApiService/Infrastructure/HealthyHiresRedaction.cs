using Kernel.Telemetry;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace ApiService.Infrastructure;

/// <summary>Registers the fail-closed redactors used by the API logging pipeline.</summary>
internal static class HealthyHiresRedaction
{
    /// <summary>
    /// Erases both HealthyHires classifications and any future classification that has not yet been assigned an
    /// explicit redactor. Logging must fail closed when taxonomy and host configuration drift.
    /// </summary>
    public static IServiceCollection AddHealthyHiresRedaction(this IServiceCollection services) =>
        services.AddRedaction(redaction => redaction
            .SetRedactor<ErasingRedactor>(
                new DataClassificationSet(HealthyHiresDataClassification.PersonalData),
                new DataClassificationSet(HealthyHiresDataClassification.SensitiveData))
            .SetFallbackRedactor<ErasingRedactor>());
}
