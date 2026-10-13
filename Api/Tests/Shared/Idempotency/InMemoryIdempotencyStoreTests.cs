using System.Text;
using Kernel.Idempotency;
using Microsoft.Extensions.Options;

namespace Tests.Shared.Idempotency;

/// <summary>
/// Tests the Shared/Kernel <see cref="InMemoryIdempotencyStore"/> state machine: a first claim starts, a
/// repeat while in-flight reports in-progress, a completed entry replays its record, the same key with a
/// different fingerprint is a mismatch, and an abandoned/expired key can be re-claimed. Also pins the two
/// properties the memory budget rests on — completing charges the response against the cache without
/// throwing for a missing size, and it does not extend the TTL the claim was made under.
/// </summary>
public class InMemoryIdempotencyStoreTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InMemoryIdempotencyStore NewStore(IdempotencyOptions? options = null) =>
        new(Options.Create(options ?? new IdempotencyOptions()));

    private static byte[] Fingerprint(string value) => Encoding.UTF8.GetBytes(value);

    private static IdempotencyRecord SampleRecord() =>
        new(200, "application/json", null, Encoding.UTF8.GetBytes("{\"ok\":true}"));

    [Fact]
    public async Task A_claim_granted_when_the_cache_is_full_still_excludes_duplicates()
    {
        // One in-flight claim fills the cache and cannot be evicted to make room for the next one.
        using InMemoryIdempotencyStore store = NewStore(new IdempotencyOptions { CacheBudgetBytes = 512 });
        Assert.Equal(IdempotencyBeginStatus.Started,
            (await store.BeginAsync("occupant", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
        Assert.Equal(IdempotencyBeginStatus.Started,
            (await store.BeginAsync("overflow", Fingerprint("fp"), Ttl, Lease, Ct)).Status);

        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await store.BeginAsync("overflow", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await store.BeginAsync("occupant", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task First_claim_starts()
    {
        using InMemoryIdempotencyStore store = NewStore();

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        Assert.Null(result.Record);
    }

    [Theory]
    [InlineData("fp", IdempotencyBeginStatus.InProgress)]
    [InlineData("different", IdempotencyBeginStatus.Mismatch)]
    public async Task Expired_ttl_does_not_end_a_live_lease(string fingerprint, IdempotencyBeginStatus expected)
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), TimeSpan.FromMilliseconds(-1), Lease, Ct);

        Assert.Equal(expected, (await store.BeginAsync("k", Fingerprint(fingerprint), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Completing_after_ttl_releases_the_claim_without_caching_an_expired_response()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), TimeSpan.FromMilliseconds(-1), Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Abandon_does_not_remove_a_completed_response()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);
        await store.AbandonAsync("k", Ct);

        Assert.Equal(IdempotencyBeginStatus.Completed, (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Second_claim_while_in_flight_is_in_progress()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.InProgress, result.Status);
    }

    [Fact]
    public async Task Claim_after_complete_replays_record()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        IdempotencyRecord record = SampleRecord();
        await store.CompleteAsync("k", record, Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Completed, result.Status);
        Assert.Same(record, result.Record);
    }

    [Fact]
    public async Task Same_key_different_fingerprint_is_mismatch()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp-a"), Ttl, Lease, Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Mismatch, result.Status);
        Assert.Null(result.Record);
    }

    [Fact]
    public async Task Mismatch_is_detected_even_after_completion()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp-a"), Ttl, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Mismatch, result.Status);
    }

    [Fact]
    public async Task Abandon_lets_the_key_be_reclaimed()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        await store.AbandonAsync("k", Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
    }

    [Fact]
    public async Task Distinct_keys_do_not_interfere()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k1", Fingerprint("fp"), Ttl, Lease, Ct);

        IdempotencyBeginResult other = await store.BeginAsync("k2", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, other.Status);
    }

    [Fact]
    public async Task Complete_on_an_unclaimed_key_is_a_no_op()
    {
        using InMemoryIdempotencyStore store = NewStore();

        await store.CompleteAsync("never-claimed", SampleRecord(), Ct); // e.g. the claim expired while the endpoint ran

        Assert.Equal(IdempotencyBeginStatus.Started, (await store.BeginAsync("never-claimed", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Complete_does_not_extend_the_ttl_set_at_begin()
    {
        // Completing re-inserts the entry to charge the response body against the budget. It has to carry
        // the original absolute expiry: a relative TTL here would silently restart the clock on every write.
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), TimeSpan.FromMilliseconds(150), Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        await Task.Delay(TimeSpan.FromMilliseconds(400), Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Completing_charges_the_response_against_the_budget()
    {
        // The cache carries a SizeLimit, so every insert must declare a size — a missing one throws. This
        // pins that both inserts (the claim and the completion) do.
        using InMemoryIdempotencyStore store = NewStore(new IdempotencyOptions { CacheBudgetBytes = 64 * 1024 });
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Exception? failure = await Record.ExceptionAsync(() =>
            store.CompleteAsync("k", new IdempotencyRecord(200, "application/json", null, new byte[8 * 1024]), Ct));

        Assert.Null(failure);
        Assert.Equal(IdempotencyBeginStatus.Completed, (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_response_beyond_the_whole_budget_is_not_retained()
    {
        // Misconfiguration guard: MaxCachedResponseBytes normally keeps bodies well under the budget, but if
        // one does exceed it the entry must not survive as an in-flight claim answering every retry with 409.
        using InMemoryIdempotencyStore store = NewStore(new IdempotencyOptions { CacheBudgetBytes = 4 * 1024 });
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        await store.CompleteAsync("k", new IdempotencyRecord(200, "application/json", null, new byte[16 * 1024]), Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Concurrent_claims_on_one_key_yield_exactly_one_start()
    {
        // The interface requires this of every implementation; nothing else in the suite exercised it.
        using InMemoryIdempotencyStore store = NewStore();
        byte[] fingerprint = Fingerprint("fp");
        IdempotencyBeginResult[] results = new IdempotencyBeginResult[64];

        await Parallel.ForAsync(0, results.Length, Ct, async (i, ct) =>
            results[i] = await store.BeginAsync("k", fingerprint, Ttl, Lease, ct));

        Assert.Equal(1, results.Count(static r => r.Status == IdempotencyBeginStatus.Started));
        Assert.Equal(results.Length - 1, results.Count(static r => r.Status == IdempotencyBeginStatus.InProgress));
    }

    [Fact]
    public async Task A_claim_whose_lease_expired_can_be_taken_over()
    {
        // The request that held it died without completing or abandoning. Without a takeover window every
        // retry of this key would be answered 409 until the whole TTL elapsed.
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, TimeSpan.FromMilliseconds(100), Ct);

        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);
        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        Assert.True(result.Reclaimed); // counted, because it means a request died mid-flight
    }

    [Fact]
    public async Task A_live_lease_is_not_taken_over()
    {
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.InProgress, result.Status);
        Assert.False(result.Reclaimed);
    }

    [Fact]
    public async Task A_completed_entry_is_replayed_regardless_of_the_lease()
    {
        // The lease governs unfinished claims only; a captured response stays replayable for its full TTL.
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, TimeSpan.FromMilliseconds(100), Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);

        Assert.Equal(IdempotencyBeginStatus.Completed, (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_stranded_claim_is_not_taken_over_by_a_different_payload()
    {
        // A takeover overwrites the fingerprint, so it must not be granted to a request the mismatch rule
        // would otherwise reject.
        using InMemoryIdempotencyStore store = NewStore();
        await store.BeginAsync("k", Fingerprint("fp-a"), Ttl, TimeSpan.FromMilliseconds(100), Ct);

        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);

        Assert.Equal(IdempotencyBeginStatus.Mismatch, (await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct)).Status);
    }
}
