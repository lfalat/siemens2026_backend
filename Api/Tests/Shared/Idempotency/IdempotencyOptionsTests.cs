using Kernel.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Shared.Idempotency;

/// <summary>
/// The lease has to outlast write-back: a completion still retrying when the lease runs out lets a duplicate
/// take the key over before the response is stored. These tests pin that such a configuration is refused
/// rather than coming up looking healthy.
/// </summary>
public class IdempotencyOptionsTests
{
    private static IdempotencyOptions Resolve(Action<IdempotencyOptions>? configure = null) =>
        new ServiceCollection().AddIdempotency(configure).BuildServiceProvider()
            .GetRequiredService<IOptions<IdempotencyOptions>>().Value;

    [Fact]
    public void The_defaults_are_valid()
    {
        IdempotencyOptions options = Resolve();

        Assert.True(options.ClaimLease > options.WriteBackTimeout);
    }

    [Theory]
    [InlineData(5, 5)]  // equal: the lease ends exactly when write-back gives up
    [InlineData(5, 30)]
    public void A_lease_not_exceeding_the_write_back_timeout_is_refused(int leaseSeconds, int writeBackSeconds)
    {
        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(() => Resolve(o =>
        {
            o.ClaimLease = TimeSpan.FromSeconds(leaseSeconds);
            o.WriteBackTimeout = TimeSpan.FromSeconds(writeBackSeconds);
        }));

        Assert.Contains("ClaimLease must exceed WriteBackTimeout.", refused.Failures);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_write_back_timeout_is_refused(int writeBackSeconds)
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(o =>
            o.WriteBackTimeout = TimeSpan.FromSeconds(writeBackSeconds)));
    }
}
