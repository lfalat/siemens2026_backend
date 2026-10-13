using Microsoft.EntityFrameworkCore;

namespace Kernel.Idempotency;

/// <summary>One claimed or completed idempotency key, as stored by <see cref="DbIdempotencyStore"/>.</summary>
public sealed class IdempotencyEntry
{
    /// <summary>The fully-scoped key (actor + method + path + client key, hashed); the primary key.</summary>
    public string Key { get; set; } = default!;

    /// <summary>SHA-256 over the request identity and body, so key reuse with a different payload is detectable.</summary>
    public byte[] Fingerprint { get; set; } = default!;

    /// <summary>
    /// Which request holds the claim. It separates a retry of the holder's own attempt from a genuine
    /// duplicate — see <see cref="DbIdempotencyStore"/>.
    /// </summary>
    public Guid ClaimOwner { get; set; }

    /// <summary>Whether the response has been captured and is replayable.</summary>
    public bool Completed { get; set; }

    /// <summary>While uncompleted: when another request may take this claim over. See <see cref="IdempotencyOptions.ClaimLease"/>.</summary>
    public DateTimeOffset LeaseExpiresAt { get; set; }

    /// <summary>When the whole record stops being replayable and may be pruned.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The captured status code; <c>null</c> while uncompleted.</summary>
    public int? StatusCode { get; set; }

    /// <summary>The captured <c>Content-Type</c>, if any.</summary>
    public string? ContentType { get; set; }

    /// <summary>The captured <c>Location</c> header, if any.</summary>
    public string? Location { get; set; }

    /// <summary>The captured response body bytes; <c>null</c> while uncompleted.</summary>
    public byte[]? Body { get; set; }

    /// <summary>When the key was first claimed (UTC); set by the database default.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// EF Core context for the shared <c>kernel</c> schema, which today holds only the idempotency records.
/// </summary>
/// <remarks>
/// A schema of its own, unlike the outbox and Push idempotency records that live in each module's schema.
/// Those belong to a module because they have to commit with that module's data. An idempotency claim is
/// taken by host middleware <em>before</em> routing has produced a handler, so there is no module to put it
/// in — it is host infrastructure, and giving it a module's schema would make that module look like its
/// owner. See ADR-D001 in <c>docs/db/decision-log.md</c>.
/// <para>
/// The table is created by <c>configs/postgres/init/01-create-db.sql</c>, not by this context (ADR-D002), so the
/// mapping below is excluded from migrations and only has to match the DDL.
/// </para>
/// </remarks>
/// <param name="options">The context options (connection, retry policy) supplied by the host.</param>
public sealed class IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options) : DbContext(options)
{
    /// <summary>The schema this context owns.</summary>
    public const string SchemaName = "kernel";

    /// <summary>The claimed and completed idempotency keys.</summary>
    public DbSet<IdempotencyEntry> Entries => Set<IdempotencyEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.Entity<IdempotencyEntry>(e =>
        {
            e.ToTable("idempotency", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Key);

            e.Property(x => x.Key).HasColumnName("key").ValueGeneratedNever();
            e.Property(x => x.Fingerprint).HasColumnName("fingerprint").IsRequired();
            e.Property(x => x.ClaimOwner).HasColumnName("claim_owner");
            e.Property(x => x.Completed).HasColumnName("completed");
            e.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.StatusCode).HasColumnName("status_code");
            e.Property(x => x.ContentType).HasColumnName("content_type");
            e.Property(x => x.Location).HasColumnName("location");
            e.Property(x => x.Body).HasColumnName("body");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();

            // Supports the retention pruner's age-based delete.
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_idempotency_expires_at");
        });
    }
}
