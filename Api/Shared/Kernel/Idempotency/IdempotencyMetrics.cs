using System.Diagnostics.Metrics;

namespace Kernel.Idempotency;

/// <summary>
/// OpenTelemetry instruments for the idempotency middleware. Registered as a singleton; the meter name is
/// exported via the host's OpenTelemetry configuration (<c>AddMeter</c>).
/// </summary>
/// <remarks>
/// Without these the feature is invisible: nothing in a log or a trace distinguishes a replayed response
/// from a fresh one, and a client stuck in a retry loop against a stranded claim looks like ordinary
/// <c>409</c> traffic. <see cref="RecordReclaim"/> counts unfinished claims taken over after lease expiry:
/// a crash, failed write-back, or slow endpoint can all cause this. Normal response TTL expiry does not.
/// </remarks>
public sealed class IdempotencyMetrics : IDisposable
{
    /// <summary>The meter name to register with OpenTelemetry (<c>AddMeter</c>).</summary>
    public const string MeterName = "HealthyHires.Idempotency";

    private readonly Meter _meter;
    private readonly Counter<long> _claims;
    private readonly Counter<long> _reclaims;
    private readonly Counter<long> _pruned;

    /// <summary>Creates the idempotency instruments on a meter from the DI <see cref="IMeterFactory"/>.</summary>
    /// <param name="meterFactory">The metrics factory.</param>
    public IdempotencyMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _claims = _meter.CreateCounter<long>("idempotency.claims", "{claim}", "Idempotency key claims, tagged by outcome.");
        _reclaims = _meter.CreateCounter<long>("idempotency.leases.reclaimed", "{claim}", "Unfinished claims taken over after their lease expired.");
        _pruned = _meter.CreateCounter<long>("idempotency.records.pruned", "{record}", "Expired idempotency records deleted by the retention job.");
    }

    /// <summary>Records the outcome of a claim.</summary>
    /// <param name="status">What the store reported.</param>
    public void RecordClaim(IdempotencyBeginStatus status) =>
        _claims.Add(1, new KeyValuePair<string, object?>("outcome", status switch
        {
            IdempotencyBeginStatus.Started => "started",
            IdempotencyBeginStatus.InProgress => "in_progress",
            IdempotencyBeginStatus.Completed => "replayed",
            _ => "mismatch",
        }));

    /// <summary>Records that a stranded claim was taken over.</summary>
    public void RecordReclaim() => _reclaims.Add(1);

    /// <summary>Records how many expired records a retention pass removed.</summary>
    /// <param name="count">The number of rows deleted.</param>
    public void RecordPruned(int count) => _pruned.Add(count);

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
