# Scheduling

Background work that runs on a timer, not in response to a request — retention pruners, reminders,
reconciliation jobs — uses the two hosts in `Kernel.Scheduling` instead of a hand-written `BackgroundService`.
A job is written as a plain **worker**; the host owns the loop, the timing, the DI scope, cancellation and
failure logging.

| Host | Runs | Use for |
| --- | --- | --- |
| `DailyWorkerService<TWorker>` | Once a day at a fixed UTC time | Work anchored to the clock: retention passes, nightly summaries |
| `PeriodicWorkerService<TWorker>` | At startup, then every interval | Work that reconciles continuously: a janitor, a reaper, a poller |

## Writing a worker

A worker implements `IScheduledWorker`: one method that runs a single pass and returns how much it did.

```csharp
public sealed partial class ExpiredDraftPruner(
    RecruitmentDbContext db,
    ILogger<ExpiredDraftPruner> logger) : IScheduledWorker
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        int removed = await db.Database.ExecuteSqlAsync(
            $"DELETE FROM recruitment.application_draft WHERE expires_at <= now()", ct);

        if (removed > 0)
            LogPruned(logger, removed);

        return removed;
    }

    [LoggerMessage(EventId = 4210, EventName = "recruitment.draft.pruned", Level = LogLevel.Information,
        Message = "Pruned {Count} expired application draft(s).")]
    private static partial void LogPruned(ILogger logger, int count);
}
```

Rules:

- **No loop, no delay, no `try`/`catch` for the schedule.** One call is one pass. The host decides when the next
  one happens and catches what escapes.
- **The worker logs its own result**, with its own `[LoggerMessage]` and an event id from its module's range. The
  host logs only failures, so per-job messages stay specific.
- **Take time from the database.** Compare against `now()` in SQL, not `DateTime.UtcNow`, so a host whose clock
  has drifted cannot delete what the rest of the system still treats as live — `IdempotencyPruner` does this.
- **Prefer one set-based statement.** It is cheap, needs no paging state between runs, and stays correct if two
  instances ever run the same job at once (see [Behaviour](#behaviour)).
- **Honour the token.** It is cancelled on shutdown; pass it to every database and HTTP call.

## Registering a worker

Register it in `Program.cs`, or in the module's registration method, with the run time or interval chosen at the
call site:

```csharp
builder.Services.AddDailyWorker<ExpiredDraftPruner>(new TimeOnly(2, 0));          // 02:00 UTC
builder.Services.AddPeriodicWorker<ScanTimeoutReaper>(TimeSpan.FromMinutes(5));   // at startup, then every 5 min
```

Both register the worker as **scoped** and the host as a singleton `IHostedService`. Scoped means every run
resolves a fresh worker — and with it a fresh pooled `DbContext` — and a test can resolve the worker directly and
call `RunAsync` without the host.

Choosing the time at the call site keeps the stagger between jobs in one place. Daily jobs share one database, so
give heavy ones their own slot. Current schedule:

| Worker | Host | When | What it does |
| --- | --- | --- | --- |
| `IdempotencyPruner` | Daily | 03:00 UTC | Deletes expired `kernel.idempotency` records ([idempotency.md](../idempotency/idempotency.md#storage)) |

Add a row here when you register a worker.

## Behaviour

**Each run** takes its own DI scope and runs inside a logging scope `Worker = <type name>`. Outside a request
there is no trace id, so this scope is what attributes EF Core's or a client library's records to the job that
caused them.

**Daily.** The delay to the next run is recomputed from the wall clock before every run: later today if the time
has not passed, otherwise tomorrow. The schedule stays anchored across restarts and long runs instead of drifting
from process start. There is **no catch-up**: if the API is down at the scheduled time, that day's run is skipped
and the next one happens the following day. A daily job must therefore tolerate a missed day — a pruner simply
deletes two days' worth next time.

**Periodic.** Runs once at startup, then on a `PeriodicTimer`. Runs never overlap: if one takes longer than the
interval, the next starts as soon as it finishes, and ticks missed meanwhile are coalesced into that one. Because
it runs at startup, every deployment triggers a pass.

**Failures.** An exception from `RunAsync` is caught and logged, and the job runs again at its next time; it never
stops the host or the API.

| Event id | Event name | Level | Meaning |
| --- | --- | --- | --- |
| `1100` | `scheduling.daily_worker.failed` | Error | A daily run threw; carries `Worker` and the exception. Retried tomorrow. |
| `1101` | `scheduling.periodic_worker.failed` | Error | A periodic run threw; carries `Worker` and the exception. Retried next interval. |

Event ids `1100–1199` are reserved for the scheduling hosts.

**Shutdown.** The token passed to `RunAsync` is cancelled when the host stops. A cancellation caused by shutdown
ends the loop quietly; any other `OperationCanceledException` is a failure like any other exception.

**Multiple instances.** Every API instance runs every registered job — there is no leader election or lock. The
API runs as a single container on the VM ([container-deployment.md §1.1](../../architecture/container-deployment.md#11-the-application-pod-healthyhires)),
so this does not arise today. A job written as a single set-based statement stays correct if it does; a job that
must run exactly once (sending mail, for example) needs a claim in the database before that changes.

## Tests

`Api/Tests/Shared/Scheduling/` covers the hosts, so a worker's own tests only need to resolve it and call
`RunAsync`: `DailyWorkerServiceTests` pins the run-time arithmetic, `PeriodicWorkerServiceTests` the startup run,
fresh scope per run, failure logging, `Worker` scope and shutdown, and `SchedulingRegistrationTests` what the
registration helpers put in the container.
