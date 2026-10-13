using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Kernel.Idempotency;

/// <summary>
/// In-memory <see cref="IIdempotencyStore"/> backed by <see cref="IMemoryCache"/> for TTL-based expiry and
/// a size budget. A single gate serialises the tiny claim/complete state transitions (the actual endpoint
/// work runs outside the lock), so a burst of same-key requests yields exactly one
/// <see cref="IdempotencyBeginStatus.Started"/>.
/// </summary>
/// <remarks>
/// Being purely in-process, entries do not survive a restart and are not shared across instances — a
/// deployment or a crash forgets every claim, so a retry that lands after one re-executes. That is the
/// trade the <c>DbIdempotencyStore</c> exists to remove; this implementation stays as the default for
/// tests and for running without a database.
/// </remarks>
/// <param name="options">Host-wide idempotency settings; the cache budget comes from here.</param>
internal sealed class InMemoryIdempotencyStore(IOptions<IdempotencyOptions> options) : IIdempotencyStore, IDisposable
{
    /// <summary>Rough per-entry overhead charged against the cache budget on top of the response body.</summary>
    private const int ClaimSize = 512;

    private readonly Lock _gate = new();

    // Its own cache, not the shared one: the budget below has to bound this feature alone, and a SizeLimit
    // another component set on the shared cache would make every write here throw.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = options.Value.CacheBudgetBytes });

    // Claims cannot share the response cache's size limit: MemoryCache silently refuses inserts when
    // full, even with NeverRemove priority. A granted claim must always exist. Expiry still bounds
    // abandoned claims; a live lease survives a shorter response TTL.
    private readonly MemoryCache _claims = new(new MemoryCacheOptions());

    /// <summary>Claim identity and, for completed entries, the captured response.</summary>
    private sealed class Entry
    {
        public required byte[] Fingerprint { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset LeaseExpiresAt { get; init; }
        public IdempotencyRecord? Record { get; set; }
    }

    /// <inheritdoc />
    public Task<IdempotencyBeginResult> BeginAsync(
        string key, byte[] fingerprint, TimeSpan ttl, TimeSpan lease, CancellationToken ct = default)
    {
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (_cache.TryGetValue(key, out Entry? completed) && completed is not null)
                return Task.FromResult(completed.Fingerprint.AsSpan().SequenceEqual(fingerprint)
                    ? new IdempotencyBeginResult(IdempotencyBeginStatus.Completed, completed.Record)
                    : new IdempotencyBeginResult(IdempotencyBeginStatus.Mismatch, null));

            bool reclaimed = false;
            if (_claims.TryGetValue(key, out Entry? existing) && existing is not null)
            {
                if (!existing.Fingerprint.AsSpan().SequenceEqual(fingerprint))
                    return Task.FromResult(new IdempotencyBeginResult(IdempotencyBeginStatus.Mismatch, null));

                if (existing.LeaseExpiresAt > now)
                    return Task.FromResult(new IdempotencyBeginResult(IdempotencyBeginStatus.InProgress, null));

                reclaimed = true;
            }

            Entry entry = new()
            {
                Fingerprint = fingerprint,
                ExpiresAt = now + ttl,
                LeaseExpiresAt = now + lease,
            };
            _claims.Set(key, entry, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = entry.ExpiresAt > entry.LeaseExpiresAt ? entry.ExpiresAt : entry.LeaseExpiresAt,
            });
            return Task.FromResult(new IdempotencyBeginResult(IdempotencyBeginStatus.Started, null, reclaimed));
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_claims.TryGetValue(key, out Entry? entry) && entry is not null)
            {
                _claims.Remove(key);
                // TTL starts at Begin, so completing after it elapsed releases the claim without
                // attempting to cache an already-expired response.
                if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    entry.Record = record;
                    _cache.Set(key, entry, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpiration = entry.ExpiresAt,
                        Size = ClaimSize + record.Body.Length,
                    });
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbandonAsync(string key, CancellationToken ct = default)
    {
        lock (_gate)
            _claims.Remove(key);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _claims.Dispose();
        _cache.Dispose();
    }
}
