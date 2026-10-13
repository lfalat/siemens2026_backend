using ApiService.Infrastructure;
using Kernel.Telemetry;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace Tests.ApiService;

/// <summary>Locks the classified-logging path used before console/journal and OTLP export (ADR-A009).</summary>
public class TelemetryRedactionTests
{
    private static LogRecord Export(Action<ILogger> write)
    {
        List<LogRecord> exported = [];
        ServiceCollection services = new();
        services.AddHealthyHiresRedaction();
        services.AddLogging(logging =>
        {
            logging.EnableRedaction();
            logging.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.ParseStateValues = true;
                options.AddInMemoryExporter(exported);
            });
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("redaction-test");
        write(logger);
        return Assert.Single(exported);
    }

    [Fact]
    public void Personal_data_argument_is_erased_before_otlp_export()
    {
        const string applicantEmail = "applicant-email-should-never-leave-the-process@example.sk";
        const string jobOffer = "nurse-icu-bratislava";

        LogRecord record = Export(logger =>
            TelemetryRedactionTestLog.ApplicationReceived(logger, applicantEmail, jobOffer));

        IReadOnlyList<KeyValuePair<string, object?>> attributes = Assert.IsAssignableFrom<
            IReadOnlyList<KeyValuePair<string, object?>>>(record.Attributes);
        KeyValuePair<string, object?> email = Assert.Single(attributes, item => item.Key == "Email");
        KeyValuePair<string, object?> jobOfferAttribute = Assert.Single(attributes, item => item.Key == "JobOffer");
        string serialized = string.Join('\n', record.Body, record.FormattedMessage, jobOfferAttribute.Value);
        Assert.DoesNotContain(applicantEmail, serialized, StringComparison.Ordinal);
        Assert.Contains(jobOffer, serialized, StringComparison.Ordinal);
        Assert.NotEqual(applicantEmail, email.Value);
        Assert.Contains("Redactor", email.Value?.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Sensitive_data_argument_is_erased_before_otlp_export()
    {
        const string sasUrl = "https://healthyhires.blob.core.windows.net/cv/123?sig=must-not-be-logged";

        LogRecord record = Export(logger => TelemetryRedactionTestLog.UploadUrlIssued(logger, sasUrl));

        Assert.DoesNotContain(sasUrl, record.FormattedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(record.Attributes!, pair => Equals(pair.Value, sasUrl));
    }

    [Fact]
    public void An_unmapped_classification_is_erased_by_the_fail_closed_fallback()
    {
        const string marker = "future-classification-must-fail-closed";

        LogRecord record = Export(logger => TelemetryRedactionTestLog.FutureData(logger, marker));

        Assert.DoesNotContain(marker, record.FormattedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, record.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(record.Attributes!, pair => Equals(pair.Value, marker));
    }
}

internal sealed class FutureDataAttribute() : DataClassificationAttribute(
    new DataClassification("HealthyHires.Tests", "FutureData"));

internal static partial class TelemetryRedactionTestLog
{
    [LoggerMessage(EventId = 9900, Level = LogLevel.Information,
        Message = "Application from {Email} received for {JobOffer}.")]
    public static partial void ApplicationReceived(
        ILogger logger, [PersonalData] string email, string jobOffer);

    [LoggerMessage(EventId = 9901, Level = LogLevel.Information, Message = "Upload URL {Url} issued.")]
    public static partial void UploadUrlIssued(ILogger logger, [SensitiveData] string url);

    [LoggerMessage(EventId = 9902, Level = LogLevel.Information, Message = "Future value {Value}.")]
    public static partial void FutureData(ILogger logger, [FutureData] string value);
}
