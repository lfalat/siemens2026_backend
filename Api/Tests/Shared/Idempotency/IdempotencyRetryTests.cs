using System.Data.Common;
using Kernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Tests.Shared.Idempotency;

/// <summary>Transient command failures against the real schema, with the production retry strategy enabled.</summary>
public class IdempotencyRetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("claim", false)]
    [InlineData("complete", false)]
    [InlineData("abandon", false)]
    [InlineData("complete", true)]
    [InlineData("abandon", true)]
    public async Task A_single_transient_failure_is_retried_before_giving_up(string operation, bool afterCommit)
    {
        await using IdempotencyDbContext seed = TestDb.NewIdempotencyContext();
        FailOnce interceptor = new(operation, afterCommit);
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContextOn(
            seed.Database.GetConnectionString()!, interceptor);
        IIdempotencyStore store = new DbIdempotencyStore(db);
        byte[] fingerprint = [1, 2, 3];
        IdempotencyRecord response = new(201, "application/json", "/things/1", "{}"u8.ToArray());

        // For completion/release, the claim is already recorded before the injected fault is armed.
        if (operation != "claim")
            await store.BeginAsync("k", fingerprint, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), Ct);

        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            switch (operation)
            {
                case "claim":
                    Assert.Equal(IdempotencyBeginStatus.Started,
                        (await store.BeginAsync("k", fingerprint, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), Ct)).Status);
                    break;
                case "complete":
                    await store.CompleteAsync("k", response, Ct);
                    break;
                default:
                    await store.AbandonAsync("k", Ct);
                    break;
            }
        });

        Assert.Null(failure);
        Assert.Equal(2, interceptor.Attempts);
        // Observe persisted state through another context, not the fault-injected one.
        IdempotencyEntry? entry = await seed.Entries.AsNoTracking().SingleOrDefaultAsync(Ct);
        if (operation == "abandon")
            Assert.Null(entry);
        else
        {
            Assert.NotNull(entry);
            Assert.Equal(operation == "complete", entry.Completed);
            if (operation == "complete")
                Assert.Equal(response.Body, entry.Body);
        }
    }

    [Fact]
    public async Task Retrying_an_abandon_with_a_lost_acknowledgement_preserves_a_new_owners_claim()
    {
        await using IdempotencyDbContext seed = TestDb.NewIdempotencyContext();
        IIdempotencyStore newOwner = new DbIdempotencyStore(seed);
        Guid replacementOwner = Guid.Empty;
        byte[] fingerprint = [1, 2, 3];
        FailOnce interceptor = new("abandon", afterCommit: true, async () =>
        {
            Assert.Equal(IdempotencyBeginStatus.Started,
                (await newOwner.BeginAsync("k", fingerprint, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), Ct)).Status);
            replacementOwner = (await seed.Entries.AsNoTracking().SingleAsync(Ct)).ClaimOwner;
        });
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContextOn(seed.Database.GetConnectionString()!, interceptor);
        IIdempotencyStore original = new DbIdempotencyStore(db);
        await original.BeginAsync("k", fingerprint, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), Ct);

        await original.AbandonAsync("k", Ct);

        Assert.Equal(2, interceptor.Attempts);
        Assert.Equal(replacementOwner, (await seed.Entries.AsNoTracking().SingleAsync(Ct)).ClaimOwner);
    }

    private sealed class FailOnce(string operation, bool afterCommit = false, Func<Task>? afterFirstWrite = null) : DbCommandInterceptor
    {
        public int Attempts { get; private set; }

        private void BeforeCommand(DbCommand command)
        {
            string prefix = operation switch
            {
                "claim" => "INSERT INTO kernel.idempotency",
                "complete" => "UPDATE kernel.idempotency",
                _ => "DELETE FROM kernel.idempotency",
            };
            if (command.CommandText.TrimStart().StartsWith(prefix, StringComparison.Ordinal) && ++Attempts == 1 && !afterCommit)
                throw new NpgsqlException("Injected transient failure before sending the command", new IOException("Connection lost"));
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            BeforeCommand(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            BeforeCommand(command);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (afterCommit && Attempts == 1)
            {
                if (afterFirstWrite is not null)
                    await afterFirstWrite();
                throw new NpgsqlException("Injected loss of acknowledgement after commit", new IOException("Connection lost"));
            }
            return result;
        }
    }
}
