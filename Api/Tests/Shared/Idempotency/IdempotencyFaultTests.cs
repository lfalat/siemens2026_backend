using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tests.Shared;

namespace Tests.Shared.Idempotency;

/// <summary>Fault-injection tests share a Toxiproxy port pool and hold real timing waits; keep them serial.</summary>
[CollectionDefinition(FaultInjection.Name, DisableParallelization = true)]
public sealed class FaultInjectionCollection;

/// <summary>Names the collection so the attribute and the definition cannot drift apart.</summary>
public static class FaultInjection
{
    /// <summary>The xUnit collection name for fault-injection tests.</summary>
    public const string Name = "fault-injection";
}

/// <summary>
/// Tests the idempotency store against a database link that fails mid-operation, using Toxiproxy.
/// </summary>
/// <remarks>
/// This is the one window the persistent store leaves open, and the only way to see it is to produce it:
/// the endpoint's transaction commits, and then the process loses the database before the response is
/// captured. A unit test can throw an exception at that point, but it cannot show what the row actually
/// looks like afterwards, nor that the lease is what gets the key moving again. The whole feature is built
/// on the claim outliving the request that took it, so the failure that strands a claim deserves a test
/// that really strands one.
/// </remarks>
[Collection(FaultInjection.Name)]
public class IdempotencyFaultTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Fingerprint(string value) => Encoding.UTF8.GetBytes(value);

    private static IdempotencyRecord SampleRecord() =>
        new(201, "application/json", "/api/v1/things/1", "{\"ok\":true}"u8.ToArray());

    [Fact]
    public async Task A_claim_stranded_by_a_lost_database_blocks_retries_until_its_lease_runs_out()
    {
        PostgresTestHost.SkipIfSchemaUnavailable();
        (string connectionString, ToxiproxyLink link) = PostgresTestHost.Current.CreateDatabaseThroughProxy();
        await using ToxiproxyLink _ = link;
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContextOn(connectionString);
        IIdempotencyStore store = new DbIdempotencyStore(db);

        // This store-level test records only the claim. The middleware test below also commits a business row.
        Assert.Equal(IdempotencyBeginStatus.Started,
            (await store.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);

        // Then the database goes away before the response can be captured.
        await link.CutAsync();
        using CancellationTokenSource writeBack = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        writeBack.CancelAfter(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CompleteAsync("k", SampleRecord(), writeBack.Token));
        await link.RestoreAsync();

        // The claim outlived the request that took it, so a retry is held off rather than duplicating the
        // committed work — this is the behaviour the lease exists to bound, not a bug.
        await using IdempotencyDbContext retryDb = TestDb.NewIdempotencyContextOn(connectionString);
        IIdempotencyStore retryStore = new DbIdempotencyStore(retryDb);
        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await retryStore.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct)).Status);

        // Advance only the persisted lease, avoiding a race between a two-second lease and Docker I/O.
        // The response TTL stays live: the lease, not retention, must make the key claimable again.
        await retryDb.Database.ExecuteSqlAsync(
            $"UPDATE kernel.idempotency SET lease_expires_at = now() - interval '1 second' WHERE key = 'k'", Ct);

        IdempotencyBeginResult afterLease = await retryStore.BeginAsync("k", Fingerprint("fp"), Ttl, Lease, Ct);
        Assert.Equal(IdempotencyBeginStatus.Started, afterLease.Status);
        Assert.True(afterLease.Reclaimed);
    }

    [Fact]
    public async Task Losing_the_database_after_the_endpoint_committed_still_returns_its_response()
    {
        // The unit-test version of this pins the behaviour with a store that throws on demand. This one
        // produces the failure for real — the connection is gone by the time the response is written back —
        // because the shape of the failure is the part worth being sure about: it arrives after the
        // endpoint committed, when the only correct answer left is the one the endpoint produced.
        PostgresTestHost.SkipIfSchemaUnavailable();
        (string connectionString, ToxiproxyLink link) = PostgresTestHost.Current.CreateDatabaseThroughProxy();
        await using ToxiproxyLink _ = link;
        await using IdempotencyDbContext db = TestDb.NewIdempotencyContextOn(connectionString);

        // Stands in for the endpoint's business table: what matters is a write that commits on its own
        // connection before the link goes down, not which module owns it.
        await using (NpgsqlConnection setup = new(connectionString))
        {
            await setup.OpenAsync(Ct);
            await using NpgsqlCommand create = new("CREATE TABLE public.fault_probe (id uuid PRIMARY KEY)", setup);
            await create.ExecuteNonQueryAsync(Ct);
        }

        Guid committedId = Guid.CreateVersion7();
        int executions = 0;
        RecordingLogger logger = new();
        using ServiceProvider services = new ServiceCollection().AddLogging().AddProblemDetails().AddMetrics()
            .BuildServiceProvider();
        using IdempotencyMetrics metrics = new(services.GetRequiredService<IMeterFactory>());

        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject-aa")], "Bearer"));
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/job-offers/x/applications";
        context.Request.Body = new MemoryStream("{\"note\":\"hi\"}"u8.ToArray());
        context.Request.Headers["Idempotency-Key"] = "abc";
        using MemoryStream response = new();
        context.Response.Body = response;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask, RoutePatternFactory.Parse("/api/v1/job-offers/{id}/applications"),
            order: 0, new EndpointMetadataCollection(), "test"));

        IdempotencyMiddleware middleware = new(
            async ctx =>
            {
                executions++;
                // Prove the middleware claimed the key before entering the handler. A plain Endpoint
                // used to bypass idempotency entirely and still let this test pass with HTTP 201.
                Assert.False((await db.Entries.AsNoTracking().SingleAsync(Ct)).Completed);
                await using (NpgsqlConnection business = new(connectionString))
                {
                    await business.OpenAsync(Ct);
                    await using NpgsqlCommand insert = new("INSERT INTO public.fault_probe (id) VALUES (@id)", business);
                    insert.Parameters.AddWithValue("id", committedId);
                    await insert.ExecuteNonQueryAsync(Ct);
                }
                ctx.Response.StatusCode = StatusCodes.Status201Created;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"id\":\"a-1\"}");
                await link.CutAsync(); // and now the database goes away
            },
            Options.Create(new IdempotencyOptions { ClaimLease = Lease, WriteBackTimeout = TimeSpan.FromMilliseconds(300) }),
            services.GetRequiredService<IProblemDetailsService>(), metrics, logger);

        await middleware.InvokeAsync(context, new DbIdempotencyStore(db));

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        response.Position = 0;
        Assert.Equal("{\"id\":\"a-1\"}", await new StreamReader(response, Encoding.UTF8).ReadToEndAsync(Ct));
        Assert.Equal(1, executions);
        Assert.Contains(logger.Events, entry => entry.Id == 3200 && entry.Exception is not null);

        await link.RestoreAsync();
        await using (NpgsqlConnection reader = new(connectionString))
        {
            await reader.OpenAsync(Ct);
            await using NpgsqlCommand exists = new("SELECT count(*) FROM public.fault_probe WHERE id = @id", reader);
            exists.Parameters.AddWithValue("id", committedId);
            Assert.Equal(1L, await exists.ExecuteScalarAsync(Ct));
        }
        await using IdempotencyDbContext retryDb = TestDb.NewIdempotencyContextOn(connectionString);
        IdempotencyEntry claim = await retryDb.Entries.AsNoTracking().SingleAsync(Ct);
        Assert.False(claim.Completed);
        Assert.Null(claim.Body);
        Assert.Equal(IdempotencyBeginStatus.InProgress,
            (await new DbIdempotencyStore(retryDb).BeginAsync(claim.Key, claim.Fingerprint, Ttl, Lease, Ct)).Status);
    }

    private sealed class RecordingLogger : ILogger<IdempotencyMiddleware>
    {
        public List<(int Id, Exception? Exception)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add((eventId.Id, exception));
    }
}
