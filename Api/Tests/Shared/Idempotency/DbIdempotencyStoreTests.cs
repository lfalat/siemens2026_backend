using System.Text;
using Kernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Shared;

namespace Tests.Shared.Idempotency;

/// <summary>
/// Tests the PostgreSQL-backed <see cref="IIdempotencyStore"/> against a real database: the claim state
/// machine, replay of a captured response, and the lease that lets a stranded claim be taken over.
/// </summary>
/// <remarks>
/// The lease cases are the reason this store can exist at all. In-memory, a claim stranded by a dying
/// process disappears with it; in a durable store it would otherwise answer every retry of that key with
/// <c>409</c> for the record's full TTL — turning a crash into an hour of a permanently wedged key.
/// </remarks>
public class DbIdempotencyStoreTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Expired = TimeSpan.FromMilliseconds(-1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IIdempotencyStore Store(IdempotencyDbContext db) => new DbIdempotencyStore(db);

    private static byte[] Fingerprint(string value) => Encoding.UTF8.GetBytes(value);

    private static IdempotencyRecord SampleRecord() =>
        new(201, "application/json", "/api/v1/things/1", "{\"ok\":true}"u8.ToArray());

    /// <summary>A captured response whose body names which request produced it.</summary>
    private static IdempotencyRecord Record(string owner) =>
        new(201, "application/json", "/api/v1/things/1", Encoding.UTF8.GetBytes(owner));

    [Fact]
    public async Task A_first_claim_starts()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();

        IdempotencyBeginResult result = await Store(db).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        Assert.False(result.Reclaimed);
    }

    [Fact]
    public async Task A_second_claim_while_in_flight_is_in_progress()
    {
        // A second *request*, which is a second store instance — the same instance looking again is the
        // same attempt and keeps its claim, covered separately below.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        await Store(db).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        await using IdempotencyDbContext otherDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);

        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await Store(otherDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_completed_claim_replays_the_captured_response()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Completed, result.Status);
        Assert.Equal(201, result.Record!.StatusCode);
        Assert.Equal("application/json", result.Record.ContentType);
        Assert.Equal("/api/v1/things/1", result.Record.Location);
        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(result.Record.Body));
    }

    [Fact]
    public async Task A_different_payload_under_the_same_key_is_a_mismatch()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp-a"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Mismatch,
            (await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_mismatch_is_detected_after_completion_too()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp-a"), Ttl, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        Assert.Equal(IdempotencyBeginStatus.Mismatch,
            (await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task An_abandoned_claim_can_be_reclaimed()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        await store.AbandonAsync("k", Ct);

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        Assert.False(result.Reclaimed); // the row was deleted, so this is a first claim again
    }

    [Fact]
    public async Task Abandon_leaves_a_completed_record_alone()
    {
        // Abandon releases *our* claim. Once a response is captured it belongs to whoever finished it.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        await store.AbandonAsync("k", Ct);

        Assert.Equal(IdempotencyBeginStatus.Completed,
            (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_claim_whose_lease_expired_is_taken_over()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Expired, Ct); // the holder died mid-request

        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        Assert.True(result.Reclaimed);
    }

    [Fact]
    public async Task A_stranded_claim_is_not_taken_over_by_a_different_payload()
    {
        // A takeover overwrites the stored fingerprint, so it must not be granted to a request the
        // mismatch rule would otherwise reject.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp-a"), Ttl, Expired, Ct);

        Assert.Equal(IdempotencyBeginStatus.Mismatch,
            (await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_completed_record_is_replayed_even_after_its_lease_would_have_run_out()
    {
        // The lease governs unfinished claims only; a captured response stays replayable for its full TTL.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Expired, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        Assert.Equal(IdempotencyBeginStatus.Completed,
            (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task An_expired_record_is_claimed_afresh()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp-a"), Expired, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);

        // Past its TTL the key is forgotten, so even a different payload starts cleanly rather than
        // colliding with a record nobody may replay any more.
        IdempotencyBeginResult result = await store.BeginAsync("k", Fingerprint("fp-b"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
    }

    [Fact]
    public async Task Complete_on_an_unclaimed_key_is_a_no_op()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);

        await store.CompleteAsync("never-claimed", SampleRecord(), Ct); // e.g. the record was pruned meanwhile

        Assert.Equal(IdempotencyBeginStatus.Started,
            (await store.BeginAsync("never-claimed", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task Concurrent_claims_on_one_key_yield_exactly_one_start()
    {
        // Each task gets its own context, so this is the real cross-connection race the middleware faces —
        // the thing an in-process lock cannot answer for.
        byte[] fingerprint = Fingerprint("fp");
        await using IdempotencyDbContext seed = TestDb.NewIdempotencyContext();
        string connectionString = seed.Database.GetConnectionString()!;

        IdempotencyBeginResult[] results = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            await using IdempotencyDbContext db = TestDb.NewContextOnSameDb<IdempotencyDbContext>(seed);
            return await Store(db).BeginAsync("k", fingerprint, Ttl, Lease, Ct);
        }));

        Assert.Equal(1, results.Count(static r => r.Status == IdempotencyBeginStatus.Started));
        Assert.Equal(15, results.Count(static r => r.Status == IdempotencyBeginStatus.InProgress));
    }

    [Fact]
    public async Task The_pruner_deletes_only_expired_records()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("stale", Fingerprint("fp"), Expired, Lease, Ct);
        await store.CompleteAsync("stale", SampleRecord(), Ct);
        await store.BeginAsync("live", Fingerprint("fp"), Ttl, Lease, Ct);

        IdempotencyPruner pruner = new(db, TestDb.NewIdempotencyMetrics(), NullLogger<IdempotencyPruner>.Instance);
        int removed = await pruner.RunAsync(Ct);

        Assert.Equal(1, removed);
        Assert.Equal(["live"], await db.Entries.Select(e => e.Key).ToListAsync(Ct));
    }

    [Theory]
    [InlineData("fp", IdempotencyBeginStatus.InProgress)]
    [InlineData("different", IdempotencyBeginStatus.Mismatch)]
    public async Task Expired_ttl_does_not_replace_an_unfinished_claim_with_a_live_lease(
        string retryFingerprint, IdempotencyBeginStatus expected)
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        await Store(db).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        // Model TTL elapsing before the lease, without racing a millisecond timer against the database.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE kernel.idempotency SET expires_at = now() - interval '1 second', lease_expires_at = now() + interval '1 hour' WHERE key = 'k'", Ct);
        IdempotencyEntry original = await db.Entries.AsNoTracking().SingleAsync(Ct);

        await using IdempotencyDbContext retryDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        IdempotencyBeginResult result = await Store(retryDb).BeginAsync("k", Fingerprint(retryFingerprint), Ttl, Lease, Ct);

        Assert.Equal(expected, result.Status);
        Assert.False(result.Reclaimed);
        IdempotencyEntry remaining = await db.Entries.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(original.ClaimOwner, remaining.ClaimOwner);
        Assert.Equal(original.Fingerprint, remaining.Fingerprint);
    }

    [Fact]
    public async Task The_pruner_preserves_an_unfinished_claim_with_a_live_lease_after_ttl()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        await Store(db).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE kernel.idempotency SET expires_at = now() - interval '1 second', lease_expires_at = now() + interval '1 hour' WHERE key = 'k'", Ct);
        IdempotencyEntry original = await db.Entries.AsNoTracking().SingleAsync(Ct);
        using IdempotencyMetrics metrics = TestDb.NewIdempotencyMetrics();
        IdempotencyPruner pruner = new(db, metrics, NullLogger<IdempotencyPruner>.Instance);

        Assert.Equal(0, await pruner.RunAsync(Ct));
        Assert.Equal(original.ClaimOwner, (await db.Entries.AsNoTracking().SingleAsync(Ct)).ClaimOwner);
    }

    [Fact]
    public async Task Reusing_an_expired_completed_key_is_not_a_lease_reclaim()
    {
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db);
        await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        await store.CompleteAsync("k", SampleRecord(), Ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE kernel.idempotency SET expires_at = now() - interval '1 second' WHERE key = 'k'", Ct);

        await using IdempotencyDbContext retryDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        IdempotencyBeginResult result = await Store(retryDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        // The middleware increments idempotency.leases.reclaimed from this flag.
        Assert.False(result.Reclaimed);
    }

    [Fact]
    public async Task The_same_request_seeing_its_own_claim_again_still_owns_it()
    {
        // What an EF retry produces: the INSERT committed, its acknowledgement was lost, and the context
        // re-sent the identical command. Before the claim carried an owner this answered InProgress —
        // a 409 handed to the request that holds the claim, caused by nothing but a dropped packet.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore store = Store(db); // one instance = one request; the store is scoped

        Assert.Equal(IdempotencyBeginStatus.Started,
            (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);

        IdempotencyBeginResult again = await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        Assert.Equal(IdempotencyBeginStatus.Started, again.Status);
        Assert.False(again.Reclaimed); // nothing died; this is the same attempt, not a takeover
    }

    [Fact]
    public async Task A_different_request_still_gets_in_progress()
    {
        // The other half of the same rule: ownership must not turn a real duplicate into a second start.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        await Store(db).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);

        await using IdempotencyDbContext otherDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);

        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await Store(otherDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_takeover_transfers_ownership()
    {
        // After a stranded claim is taken over, the original holder must not be able to complete it as if
        // it were still theirs.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore stranded = Store(db);
        await stranded.BeginAsync("k", Fingerprint("fp"), Ttl, Expired, Ct);

        await using IdempotencyDbContext takerDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        IIdempotencyStore taker = Store(takerDb);
        Assert.True((await taker.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Reclaimed);

        // The stranded holder now sees a live claim it does not own.
        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await stranded.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }

    [Fact]
    public async Task A_stranded_holder_cannot_complete_the_claim_it_lost()
    {
        // A request slow enough to lose its lease is still running, and will still finish. By then the key
        // belongs to whoever took it over; storing the slow request's response would hand every later retry
        // the answer of the attempt that lost the key, while the winner's own completion did nothing.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore stranded = Store(db);
        await stranded.BeginAsync("k", Fingerprint("fp"), Ttl, Expired, Ct);

        await using IdempotencyDbContext takerDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        IIdempotencyStore taker = Store(takerDb);
        Assert.True((await taker.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Reclaimed);

        await stranded.CompleteAsync("k", Record("stranded"), Ct);

        // The takeover's claim is untouched: still in flight, nothing captured.
        await using IdempotencyDbContext readerDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await Store(readerDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);

        // And the holder can still complete it with its own response.
        await taker.CompleteAsync("k", Record("taker"), Ct);
        IdempotencyBeginResult replayed = await Store(readerDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        Assert.Equal(IdempotencyBeginStatus.Completed, replayed.Status);
        Assert.Equal("taker", Encoding.UTF8.GetString(replayed.Record!.Body));
    }

    [Fact]
    public async Task A_stranded_holder_cannot_release_the_claim_it_lost()
    {
        // The worse half of the same problem. Releasing a claim held by someone else does not just lose a
        // response — it opens the key while the new holder is still working, so a third request starts
        // alongside it and the exclusion this store exists to provide is gone.
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContext();
        IIdempotencyStore stranded = Store(db);
        await stranded.BeginAsync("k", Fingerprint("fp"), Ttl, Expired, Ct);

        await using IdempotencyDbContext takerDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        Assert.True((await Store(takerDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Reclaimed);

        await stranded.AbandonAsync("k", Ct);

        await using IdempotencyDbContext thirdDb = TestDb.NewContextOnSameDb<IdempotencyDbContext>(db);
        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await Store(thirdDb).BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);
    }
}
