# Idempotency

Host middleware deduplicates writes on every `POST`/`PATCH` endpoint under the versioned API group, with no
per-endpoint wiring. While a successful response is retained, a client that retries gets the original
result and no second side effect. The guarantee is bounded: see [Limitations](#limitations) for the
gap between commit and capture, and for lease expiry. `PUT` is not covered, because it is idempotent by
HTTP semantics: repeating it lands the same resource state.

Some reads are exposed as `POST` because their filters and cursor do not fit a query string. They are
mapped with `MapQuery` and are not covered — see [Reads exposed as POST](#reads-exposed-as-post).

The mechanism is the standard `Idempotency-Key` request header. The client generates a unique key per
logical operation and sends the **same** key again when it retries. The middleware lives in the shared kernel
(`Kernel.Idempotency`) and stores claims in **PostgreSQL**, so a claim outlives the process that took
it and every instance sees the same one ([ADR-A016](../../architecture/ADR-ASPNET.md#adr-a016-idempotency--idempotency-key-middleware)).

## Request contract

| Situation | Response |
| --- | --- |
| First request with a key | Runs normally; a **successful (2xx)** response is remembered. |
| Repeat with the same key + same payload | The remembered response is **replayed byte-for-byte**, with an `Idempotency-Replayed: true` header. The endpoint does not run again. |
| Repeat while the first claim has a live lease | `409 Conflict`. The claim is still held, which also happens when capturing the response failed. |
| Same retained key, **different** payload | `422 Unprocessable Entity`; see [Claim lifecycle](#claim-lifecycle) for expiry rules. |
| `POST`/`PATCH` with **no** key header | `400 Bad Request` (configurable, see below). |
| Non-success response (4xx/5xx) | **Not** remembered; a later retry re-runs the endpoint. |
| Successful response above the size cap | Sent to the client, **not** remembered; a later retry re-runs the endpoint. |
| Key longer than the length limit | `400 Bad Request`, before the store is touched. |
| `GET`/`PUT`/`DELETE`, or an opted-out endpoint | Passes straight through, untouched. |
| `POST`/`PATCH` to a path that does not accept it | Passes straight through to the `405`, which does not mention the missing key. |

The middleware's own rejections are ProblemDetails responses with a Slovak `detail`, like every other error
([errors.md](../errors/errors.md)).

Keys are scoped **per signed-in user and per method + path**, so the same key value used by two staff
users, or on two different endpoints, never collides. The user is the token's subject, the same identity
the rate limiter partitions by. Anonymous applicants share one scope per method + path, so their keys must
be random UUIDs that cannot be guessed.

The last row is why the middleware matches on `RouteEndpoint` and not on any endpoint at all. When a
path exists but rejects the method, routing selects a non-routable endpoint that answers `405`. Treating
that as a covered write would tell a `POST` to a read-only path to supply a header, for an operation the
OpenAPI document does not declare, and the Schemathesis run would flag it
([ADR-009](../../architecture/ADR.md#adr-009-api-testing--contract-validation--schemathesis)).

> **Note:** error responses produced by *throwing* are written by the exception handlers
> (`UseExceptionHandler`). Those sit outside this middleware, so their responses are never cached. Only
> responses an endpoint *returns* (normal `IResult`s) are captured, and of those only 2xx.

## Defaults

- **Header:** `Idempotency-Key` (request), `Idempotency-Replayed` (response, on a replay).
- **TTL:** the replay window ends **1 hour after the claim starts**. After that a completed key may be reused; an unfinished claim stays protected until its lease expires.
- **Claim lease:** **1 minute from acquisition or takeover**, never renewed automatically. It must be longer than endpoint execution, response capture and write-back together, including retry delays.
- **Write-back timeout:** a **5-second cancellation budget** for completing or releasing a claim, including retry delays. It does not depend on whether the client is still connected, and the provider can take extra time to unwind after cancellation. The claim lease must be longer, or the host fails at startup: write-back runs inside the lease, and if the lease ends first, a duplicate can take over a key whose response is still being stored.
- **Key required:** yes; a covered write without a key gets a `400`.
- **Key length:** at most 128 characters, comfortably above the UUID or base64 128-bit value that clients actually send. The key comes from the caller and ends up in a cache key, so it is bounded here rather than left to the header-size limit.
- **Memory budget (in-memory store only):** 64 MB of remembered responses. Beyond that the store evicts completed entries or declines to store them, and a key whose completed entry is gone runs again. In-flight claims live in a separate cache with no size limit and expire at the later of TTL and lease, so pressure on the response cache cannot silently drop a granted claim.
- **Largest remembered response:** 256 KB. A larger successful response is still sent, just not remembered.

Each of these can be changed when the feature is registered:

```csharp
builder.Services.AddIdempotency(options =>
{
    options.DefaultTtl = TimeSpan.FromHours(1);
    options.ClaimLease = TimeSpan.FromMinutes(1);
    options.WriteBackTimeout = TimeSpan.FromSeconds(5);
    options.HeaderName = "Idempotency-Key";
    options.RequireKey = true; // set false to make idempotency opt-in per request
    options.MaxKeyLength = 128;
    options.CacheBudgetBytes = 64L * 1024 * 1024;
    options.MaxCachedResponseBytes = 256 * 1024;
});
```

## Per-endpoint overrides

Both overrides live in `Kernel.Idempotency` and can be chained onto any endpoint, or onto a whole route group:

**Custom TTL**: remember this endpoint's responses for a different window than the default:

```csharp
group.MapPost("/things", Handler)
     .WithIdempotencyTtl(TimeSpan.FromMinutes(5)); // overrides the 1h default for this endpoint
```

**Opt out**: for endpoints that have their own dedup contract, or that are not business writes, so they
should not require the header:

```csharp
app.MapHub<NotificationsHub>("/hubs/notifications")
   .DisableIdempotency(); // the SignalR negotiate POST is a transport handshake, not a write
```

Every SignalR hub ([ADR-A014](../../architecture/ADR-ASPNET.md#adr-a014-real-time-communication--signalr))
must be opted out. Otherwise its negotiate `POST` is covered, and the client, which sends no key, gets a `400`.

## Reads exposed as POST

Some endpoints are safe, idempotent reads that carry a request body, because their filters, id arrays and
keyset cursor do not fit a query string. Map them with `MapQuery`, not `MapPost`:

```csharp
group.MapQuery("/job-offers/search", Handler)
```

`MapQuery` maps a `POST` route and attaches `DisableIdempotency`. A query needs no at-most-once protection,
must not demand a key, and must never replay a result set captured up to a TTL ago. Left covered, every
search response would sit in the store for the whole TTL, under a key the client never repeats.

[ADR-A015](../../architecture/ADR-ASPNET.md#adr-a015-read-only-endpoints-with-a-request-body--post-now-query-later)
records the decision and the later move to the `QUERY` method, which is what these endpoints really are.
`QueryEndpointOptOutTests` will fail the build when a `POST`/`PATCH` endpoint dispatches a query without
being opted out, or dispatches a command while being opted out. It arrives with the first module
(`TODO(first-module)`, [first-module-tests.md](../first-module-tests.md)).

## How it fits the pipeline

`AddIdempotency()` registers the options, the metrics and the in-memory store. `AddDbIdempotencyStore(...)`
then replaces that store with the PostgreSQL one. `UseIdempotency()` places the middleware in `Program.cs`
after the rate limiter and **before** the endpoints execute. Once staff sign-in exists it must also sit
**after** authentication, so that keys are scoped to the resolved user.

For a covered request the middleware:

1. Computes a SHA-256 fingerprint over method + path + query + body. It buffers the body so model binding can
   still read it, and streams it through the hash instead of copying it.
2. Claims the key in the store.
3. Replays the stored response, rejects the request, or runs the endpoint with a buffered response that it
   can capture on success.

The cache key is a hash of actor + method + path + key. Joining those parts with a delimiter would not be
safe, because `:` is legal in a path and the key comes from the caller.

The contract is part of the OpenAPI document. `IdempotencyHeaderTransformer`, an operation transformer
registered in `Program.cs`, declares the header and the `400`/`409`/`422` rejections on exactly the operations
the middleware covers. It reads `IdempotencyMiddleware.IsCoveredMethod` and the same `IdempotencyDisabled`
metadata, so the document cannot drift from what is enforced. An endpoint that documents its own `409`
keeps its own description.

The outcome is recorded **before** the buffered response is flushed to the client, not after. Flushing is
the step most likely to fail, and a dropped connection is exactly what a retry follows. The work has
already committed by then, so an entry left in flight would answer that retry with `409` until its lease
expires. Recording first turns the retry into a replay.

## Limitations

- **The response is captured only after the endpoint's transaction commits.** A crash in between
  leaves the work done and the response lost; once the claim's lease runs out, the retry runs the work again.
  Closing that window needs a natural key on the business row (a unique constraint enforced by the handler's
  own transaction), not a bigger idempotency layer, because the response only exists after the commit.
- **2xx only.** By design, only successful responses are replayed; failures always run again.
- **Buffered responses.** The middleware buffers the whole response body in memory to capture it, and
  does not store one larger than `MaxCachedResponseBytes` (256 KB); such a request succeeds but cannot be
  replayed. A covered endpoint therefore cannot stream a `POST` response.
- **Only some headers replay.** `Content-Type`, `Location` and `Content-Length` are restored; any other
  header the endpoint set is not.
- **The lease must be longer than endpoint execution, response capture and write-back together.** The lease
  is what lets another request take over a claim stranded by a dying process, instead of getting `409` for
  the rest of the TTL. But if the lease expires while the work is still running, a concurrent duplicate is
  let in. The slow request cannot damage the one that replaced it, though: completion and release are scoped
  to `claim_owner`, so an attempt that lost the key finds nothing to act on and cannot overwrite the winner's
  response or delete its claim.
- **An exception releases the claim, even one thrown after the endpoint committed.** The middleware cannot
  tell a handler that failed (nothing committed, so releasing is right) from one that returned before
  something downstream threw, response serialization for example. It releases in both cases, so in the
  second one a retry repeats work that already landed. Holding the claim instead would be the opposite
  mistake: every request that legitimately failed would wait out a whole lease before it could be retried.
  If the store itself fails at that point, the failure is logged and the response is still returned,
  because by then the only correct answer left is the one the endpoint produced.
- **A record pruned between the claim and the read is reported as `409`.** The two statements in
  `BeginAsync` do not run in one transaction. If a retention pass lands between them, the key looks held
  when nobody holds it. The window is milliseconds against a job that runs once a day. The correct
  behaviour would be to try the claim again, which has not been worth the second round trip.

## Storage

Claims and captured responses live in [`kernel.idempotency`](../../db/modules/kernel.md#idempotency). The
schema belongs to the host, not to a module, because a claim is taken before routing has chosen a handler.
The reasoning and the table's autovacuum settings are in
[ADR-D001](../../db/decision-log.md#adr-d001--idempotency-records-get-a-shared-kernel-schema-not-a-modules).

The table is created by `configs/postgres/init/01-create-db.sql`, which the AppHost loads into the local database
on every run ([ADR-D002](../../db/decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production)).
`IdempotencyDbContext` maps it with `ExcludeFromMigrations`, so a change to the table is a change to that script.

`IdempotencyPruner` deletes expired records daily at 03:00 UTC ([scheduling.md](../scheduling/scheduling.md)) and keeps unfinished claims whose lease is
still live. An unfinished key with a live lease keeps rejecting duplicates and changed payloads, even after
its TTL has passed. Once both TTL and lease have expired, the key can be reused with a new payload.

### Claim lifecycle

When a request tries to claim a key, the PostgreSQL store applies these rules atomically:

| Stored state | Same payload | Different payload |
| --- | --- | --- |
| No record | Start execution | Start execution |
| Completed, TTL live | Replay the captured response | `422` |
| Completed, TTL expired | Start execution | Start execution |
| Unfinished, lease live, regardless of TTL | `409` | `422` |
| Unfinished, lease expired, TTL live | Take over and execute | `422` |
| Unfinished, lease and TTL expired | Take over and execute | Take over and execute |

A takeover assigns a new owner and starts fresh TTL and lease windows. Completion does not extend the TTL:
if the response is captured after that window has closed, it cannot be replayed. The pruner removes
completed records once their TTL has passed, and unfinished records only once both TTL and lease have expired.

An expired lease allows a takeover, but it does not erase the owner by itself. Until another request takes
over or the row is pruned, the original owner can still complete or abandon its claim. Once ownership
changes, its completion and abandonment do nothing.

### Store registration and write-back

`AddIdempotency()` on its own registers the in-memory store, which is what tests and database-less runs
use. `AddDbIdempotencyStore(...)` replaces it, and receives the host's `NpgsqlDataSource`, so the context
signs in with the VM's managed identity in production ([ADR-006](../../architecture/ADR.md#adr-006-primary-database--postgresql-managed)).
The middleware resolves the store per request, not in its constructor, because the database store is scoped.

That scoping does more than keep the context safe. Each store instance carries a `claim_owner`, so a claim
belongs to one request and not to the store in general. This separates a retry of the holder's own attempt
from a genuine duplicate. The context retries on transient failures, so an `INSERT` that committed but lost
its acknowledgement is sent again. Without an owner, the store would answer that retry with `409`,
reporting a conflict to the very request that holds the claim.

The owner also decides who may finish a claim. `CompleteAsync` and `AbandonAsync` both match on it. A
request whose claim was taken over while it was still running therefore finds nothing to write to: it
cannot store the response of the attempt that lost the key, or release a claim someone else is working
under. The in-memory store cannot make this distinction, because it is a singleton and its claims carry
no owner. A slow old attempt can still affect a replacement claim in that store, so use the PostgreSQL
store whenever ownership across lease takeovers matters.

Completion and abandonment explicitly use the configured EF execution strategy, because raw SQL does not
invoke it automatically. Both statements match the request's owner, so retrying after a lost
acknowledgement cannot overwrite another owner's response or delete its claim. The middleware passes
`WriteBackTimeout` to bound how long retries can run. If write-back still fails, the endpoint's response
or original exception is preserved.

The timeout is a cooperative cancellation budget for the store call, not a hard five-second HTTP deadline,
and it neither cancels nor rolls back the business transaction. If storing the captured response fails or
runs out of budget, the middleware logs the failure and still tries to send the endpoint's response.
Two outcomes are possible:

- **Completion never reached the database.** The claim stays unfinished until it is taken over or pruned.
- **Only the acknowledgement was lost.** The record may already be completed.

If releasing a claim fails while an endpoint exception is being handled, the original exception is kept.
The [commit-to-capture limitation](#limitations) still applies.

## Observability

`IdempotencyMetrics` creates the meter `HealthyHires.Idempotency`, which is registered in
`ServiceDefaults`. It counts:

| Instrument | What it counts |
| --- | --- |
| `idempotency.claims` | Claims, tagged `outcome` = `started` / `in_progress` / `replayed` / `mismatch` |
| `idempotency.leases.reclaimed` | Unfinished claims taken over after their lease expired; normal expiry of completed responses is not counted |
| `idempotency.records.pruned` | Expired records deleted by the retention job |

A sustained rate of `idempotency.leases.reclaimed` is worth investigating: it points to process crashes,
failed write-backs, or endpoints that outlive their lease. The database store tells these outcomes apart
atomically using PostgreSQL 18's [old-row values in `RETURNING`](https://www.postgresql.org/docs/18/dml-returning.html).

The middleware logs a failed write-back (event `3200`) and a failed release (`3201`) as errors, an
oversize response that could not be stored (`3202`) as a warning, and each retention pass (`3210`).

## Tests

The tests live in `Api/Tests/Shared/Idempotency/` and `Api/Tests/ApiService/OpenAPI/IdempotencyHeaderTransformerTests.cs`:

| Tests | What they pin |
| --- | --- |
| `IdempotencyMiddlewareTests` | The request contract above against a scripted store: key rules, pass-through, replay, 409/422, capture rules, actor scoping, and the write-back failure paths |
| `DbIdempotencyStoreTests` | The [claim lifecycle](#claim-lifecycle) against the real schema: TTL versus lease, takeover, `claim_owner` guards, concurrent claims, and the pruner |
| `IdempotencyRetryTests` | A transient failure before a command, or a lost acknowledgement after it, is retried without disturbing a new owner |
| `IdempotencyFaultTests` | A real link cut through Toxiproxy: a claim stranded until its lease runs out, and an endpoint's committed response still returned |
| `InMemoryIdempotencyStoreTests` | The same state machine in the in-memory store, plus its memory budget |
| `IdempotencyOptionsTests` | A lease not longer than the write-back timeout fails at startup |
| `IdempotencyHeaderTransformerTests` | The OpenAPI document declares the header and rejections on exactly the covered operations |
