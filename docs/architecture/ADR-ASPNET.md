# Decision Log (ADR) for ASP.NET Monolith

This document captures architecture decisions specific to the ASP.NET monolith itself. It intentionally excludes platform-wide infrastructure decisions that belong in the main ADR log.

Each entry follows the same structure:

- Title
- Status
- Context
- Decision
- Consequences
- Dates & Owner

---

## ADR-A001: Application Architecture — Modular Monolith

Status: Accepted

Context:

- The application should keep low coupling and high cohesion as it grows, and allow parts of it to be split into microservices later if needed, without paying the operational cost of microservices now.

Decision:

- Build the application as a modular monolith: one deployable unit composed of independent business modules.

Consequences:

- Each module is its own .NET class library project with its own endpoint mapping; vertical slices ([ADR-A003](#adr-a003-application-architecture--vertical-slice-cqrs-and-decorators)) live inside modules.
- Each module has its own EF Core `DbContext` mapped to its own PostgreSQL schema in the shared database; modules never read or write another module's schema.
- Modules must not communicate directly with each other, only through shared abstractions (contracts).
- Inter-module communication is in-process for now: synchronous calls through contract interfaces, asynchronous through in-process messaging.
- If a module is split out, its contract interfaces become gRPC calls and its in-process messages move to message broker queues.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A002: Application Platform — ASP.NET on .NET 10

Status: Accepted

Context:

- The monolith is built on the Microsoft stack and should stay close to the framework defaults where possible.
- The team wants a long-lived baseline with modern ASP.NET features and a clear upgrade path.

Decision:

- Build the monolith with ASP.NET on .NET 10.

Consequences:

- We can rely on the built-in ASP.NET hosting, DI, OpenAPI, validation, rate limiting, resilience, and telemetry integration points.
- Future upgrades should stay aligned with Microsoft-supported primitives instead of framework-specific abstractions layered on top.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A003: Application Architecture — Vertical Slice, CQRS, and Decorators

Status: Accepted

Context:

- The codebase should stay easy to navigate as the monolith grows.
- The team wants explicit command/query separation without minimal dependencies.

Decision:

- Organize the monolith around vertical slices and CQRS.
- Use dependency injection decorators for cross-cutting concerns and pipeline-style behavior instead of MediatR, with Scrutor scanning each module for handlers and registering the decorators ([implementation-guide.md](../api/implementation-guide.md)).

Consequences:

- Features stay localized, which reduces coupling between unrelated parts of the application.
- Commands and queries are easier to reason about, but the team must maintain consistent conventions for slice boundaries.
- Cross-cutting behavior such as validation, logging, and retries must be implemented in a disciplined decorator chain.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A004: API Style — REST, OpenAPI, Scalar, and Versioning

Status: Accepted

Context:

- The API must stay discoverable for consumers and testable in CI.
- The team needs a consistent contract publishing strategy and a simple documentation UI.

Decision:

- Use REST-style endpoints and generate OpenAPI documentation from ASP.NET `Microsoft.AspNetCore.OpenApi`.
- Use Scalar as the API documentation UI `Scalar.AspNetCore`.
- Use `Asp.Versioning` for API versioning.

Consequences:

- API changes must remain backward compatible or be versioned explicitly.
- OpenAPI becomes the source of truth for contract tests such as Schemathesis ([ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis)).
- Endpoint metadata must stay accurate so documentation and versioning remain reliable. Being generated makes the document trustworthy about what exists, not about what it means — everything the framework cannot infer is declared by a transformer or by endpoint metadata, and is only as right as the declaration.
- The document describes sequences, not only calls: an endpoint whose response carries an id another operation takes in its route declares an OpenAPI link with `.ProducesLink(...)` ([implementation-guide.md §5.1](../api/implementation-guide.md#51-linking-a-response-to-what-it-unlocks)). Schemathesis follows these links to chain calls in its stateful tests ([ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis)).

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A005: Persistence — PostgreSQL with Entity Framework Core

Status: Accepted

Context:

- The application needs a relational store with strong ecosystem support and migration tooling.
- The stack already uses managed PostgreSQL as the primary data store ([ADR-006](ADR.md#adr-006-primary-database--postgresql-managed)).

Decision:

- Use PostgreSQL as the primary database.
- Use Entity Framework Core for persistence, querying, and migrations.

Consequences:

- Domain changes can be expressed through EF Core migrations.
- Performance-sensitive queries may still require explicit SQL or careful query shaping.
- Schema changes must be coordinated with release processes and backup/restore expectations.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A006: Caching — HybridCache

Status: Accepted

Context:

- The monolith needs a simple caching abstraction for hot reads and expensive computations.
- The cache implementation should work well in a single-process or small-deployment setup.

Decision:

- Use HybridCache (L1 cache) for application caching.

Consequences:

- The application gets a straightforward cache API with room for local and distributed cache strategies.
- Cache invalidation rules must be documented per feature slice to avoid stale data bugs.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A007: Rate Limiting — ASP.NET Core Rate Limiting

Status: Accepted

Context:

- Basic endpoint throttling is required for abuse protection and request shaping.
- No CDN or WAF sits in front of the application; Caddy is the public edge ([ADR-003](ADR.md#adr-003-edge--reverse-proxy--caddy)), so application-level rate limiting is the only throttling layer.
- Applicants use the API anonymously ([ADD §2](ADD.md#2-purpose)), so public endpoints must be limited per client, not only per signed-in user.

Decision:

- Use ASP.NET Core `AspNetCore.RateLimiting`, partitioned by the **authenticated user** (the `sub` claim) for staff and by **client IP** for anonymous callers, with a stricter budget on the anonymous application endpoints.
- Recover the client IP with the `ForwardedHeaders` middleware, trusting `X-Forwarded-For` only from Caddy as the single known proxy.
- The budget itself — permits, window, queue depth — is **configuration** (`RateLimiting:*`, bound to `RateLimitOptions`), not a literal in the pipeline.

Consequences:

- The application can enforce per-endpoint limits without extra middleware dependencies.
- `UseForwardedHeaders()` must run first and `UseRateLimiter()` **after** `UseAuthentication()`, or the client IP or principal does not exist when the partition key is computed.
- The client IP is only trustworthy because Caddy is the sole trusted proxy and overwrites `X-Forwarded-For` from clients; trusting the header from any other source would let callers choose their own partition key. Without it, every anonymous request would share Caddy's address and one spammer could block all applicants.
- Applicants behind the same NAT share an IP budget; acceptable at this scale.
- Rate limit policy design must match the public API surface and expected client behavior.
- Alerts or logs should distinguish genuine traffic spikes from abusive patterns.
- Per-endpoint budgets (a search costs far more than a lookup) are deliberately deferred until there are real traffic figures to size them with.
- The defaults are unchanged (100 requests per minute per caller, five queued). Making them configurable is what allows a load test to measure the API rather than the limiter ([ADR-018](ADR.md#adr-018-load-and-performance-testing--k6)), and a contract sweep ([ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis)) to avoid throttled cases that pass every conformance check with no endpoint behind them. Bounds are declared on the options and validated at startup, so a limiter configured to permit everything fails to come up rather than looking healthy.
- The queue is the part to review before raising anything. A queued request waits rather than being refused, and on a fixed window it can wait out the entire window — so an over-budget caller sees a request hang for up to a minute instead of a prompt `429` carrying `Retry-After`. Setting `QueueLimit` to `0` trades burst tolerance for an honest answer.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A008: Validation — Native ASP.NET / .NET 10 Validation

Status: Accepted

Context:

- Request validation should be lightweight, explicit, and close to the endpoint handler.
- The team wants the ergonomics of modern API frameworks without introducing a separate validation stack unless needed.

Decision:

- Use native ASP.NET / .NET 10 validation for request validation, model binding, and endpoint-level checks.

Consequences:

- Validation rules remain close to the API surface and can be applied consistently across slices.
- Complex domain validation may still be implemented inside the application layer, but request validation should stay simple and predictable.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A009: Telemetry — OpenTelemetry with EF Core Integration

Status: Accepted

Context:

- The system must expose traces and metrics with enough detail to debug performance and failures.
- Database access is a critical path and should be visible in telemetry.

Decision:

- Use OpenTelemetry for traces, metrics, and logs, exported through the local Collector to Azure Monitor ([ADR-007](ADR.md#adr-007-observability--azure-monitor--opentelemetry), [ADR-007.1](ADR.md#adr-0071-telemetry-pipeline--opentelemetry-collector-otelcol-contrib)).
- Integrate EF Core instrumentation so database activity is visible in traces and metrics.

Consequences:

- The team can trace a request through the API and into the database layer.
- Telemetry configuration must avoid leaking PII and must be reviewed as the schema evolves.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A010: File Storage — Azure Blob Storage via Azure SDK

Status: Accepted

Context:

- Modules need to store user-uploaded files. The platform uses Azure Blob Storage, with Azurite for local development ([ADR-005](ADR.md#adr-005-object-storage--azure-blob-storage)).

Decision:

- Access Blob Storage through `Azure.Storage.Blobs`, registered once via `Microsoft.Extensions.Azure` with `DefaultAzureCredential` (managed identity in production, Azurite connection string locally).
- Each module uses its own blob container, behind its own storage abstraction.
- Browsers upload and download files directly to and from Blob Storage using short-lived user delegation SAS URLs, issued by the API only after it has authorized the request (signed via managed identity, no account keys).

Consequences:

- Container-per-module mirrors schema-per-module ([ADR-A001](#adr-a001-application-architecture--modular-monolith)); modules never access another module's container.
- File metadata (name, owner, content type) lives in the module's database schema; blobs are stored under generated IDs, not user file names.
- Upload: the client requests an upload URL with the file's size and content type; the API checks permissions and validates both against module rules, then returns a write-only SAS URL for one new blob.
- A SAS URL cannot enforce file size, so after upload the client confirms it and the API checks the blob's actual size and type; files that don't match are deleted and never become visible.
- Each issued upload URL is recorded as a pending upload in the module's schema; a background job deletes pending uploads that were never confirmed within an hour.
- For anonymous applicants ([ADD §2](ADD.md#2-purpose)), authorizing an upload means: the job offer is open, the ALTCHA solution is valid ([ADR-022](ADR.md#adr-022-bot-protection--altcha-on-the-application-form)) and the per-IP rate limit allows it ([ADR-A007](#adr-a007-rate-limiting--aspnet-core-rate-limiting)).
- After an upload is confirmed, the API streams the blob to the ClamAV container ([ADR-005](ADR.md#adr-005-object-storage--azure-blob-storage)); files stay hidden from staff until ClamAV reports them clean, and infected files are deleted. The same ClamAV container runs in the local stack.
- Download: the API checks permissions and returns a read-only SAS URL for that one blob, valid for a few minutes.
- File transfers don't load the VM, but the storage account must be reachable from browsers, with CORS allowing the app origin.
- Integration tests run against Azurite via Testcontainers.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A011: Resilience — Microsoft.Extensions.Resilience

Status: Accepted

Context:

- The application depends on database, storage, and identity services that can fail transiently.

Decision:

- Use `Microsoft.Extensions.Resilience` for retries, timeouts, circuit breaking, and related resilience policies.

Consequences:

- Resilience behavior stays aligned with the Microsoft stack and can be configured centrally.
- Policies must be tuned carefully to avoid masking real outages or causing retry storms.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A012: DTO Mapping — Manual Mapping

Status: Accepted

Context:

- The team wants explicit control over mapping behavior and fewer hidden abstractions.

Decision:

- Map DTOs manually.

Consequences:

- Mapping behavior stays transparent and easy to review.
- The codebase avoids another dependency, but mapping boilerplate must be kept consistent and tested.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A013: Build and Publish — Native Container Publish on noble-chiseled

Status: Accepted (amended 12. 10. 2026: no AOT)

Context:

- The application should be built into a production-ready container image using the standard .NET build pipeline, with a small and secure runtime image.
- A native AOT build crashes at startup: `Asp.Versioning.OpenApi` calls `Assembly.GetCallingAssembly()`, which AOT does not support. `Asp.Versioning.Mvc.ApiExplorer` pulls in MVC, which supports neither AOT nor trimming, and EF Core's AOT support is experimental ([ADR-A004](#adr-a004-api-style--rest-openapi-scalar-and-versioning), [ADR-A005](#adr-a005-persistence--postgresql-with-entity-framework-core)).

Decision:

- Use the built-in .NET publish-to-container workflow (`dotnet publish -t:PublishContainer`), framework-dependent, on the `noble-chiseled` runtime image. No AOT and no trimming.

Consequences:

- Chiseled base: no shell or package manager, non-root user; the SDK picks the `-extra` variant, which adds ICU and time-zone data.
- The image is about 250 MB instead of about 100 MB with AOT, and starts slower; neither matters for one long-running instance.
- Libraries are not restricted by AOT or trimming rules. Revisit if Asp.Versioning, MVC ApiExplorer and EF Core become AOT-compatible.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A014: Real-Time Communication — SignalR

Status: Accepted

Context:

- The application requires real-time communication for notifications, live updates, and collaborative features.
- The team wants a solution that integrates well with ASP.NET and supports WebSockets.

Decision:

- Use SignalR for real-time communication.
- Configure SignalR hubs for specific features and use the built-in ASP.NET authentication and authorization mechanisms.

Consequences:

- Real-time features can be implemented with minimal overhead and good integration with the existing ASP.NET stack.
- The team must ensure that SignalR hubs are properly secured and that scaling considerations.
- SignalR may require additional infrastructure considerations for scaling, such as using a backplane or Redis for message distribution in a multi-server setup.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A015: Read-only Endpoints with a Request Body — POST Now, QUERY Later

Status: Accepted

Context:

- Some endpoints are safe, idempotent reads whose filters, id arrays or keyset cursors don't fit in a query string, so they need a request body. They are dispatched through `IQueryHandler` ([ADR-A003](#adr-a003-application-architecture--vertical-slice-cqrs-and-decorators)) and change nothing, but HTTP gives them no honest method, so they are mapped as `POST`.
- The idempotency middleware ([ADR-A016](#adr-a016-idempotency--idempotency-key-middleware)) decides coverage from the request method alone, so it treats these reads as writes: it demands an `Idempotency-Key` and caches every response for nothing.
- RFC 10008 defines `QUERY` for exactly this shape. However, on .NET 10 ([ADR-A002](#adr-a002-application-platform--aspnet-on-net-10)) `QUERY` endpoints can't be described in OpenAPI, so they would disappear from Scalar ([ADR-A004](#adr-a004-api-style--rest-openapi-scalar-and-versioning)) and from Schemathesis coverage ([ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis)). OpenAPI 3.2 support for `QUERY` arrives in .NET 11.

Decision:

- Keep these endpoints on `POST`, mapped through a single `MapQuery` helper in `Kernel.Idempotency` that maps `POST` and attaches `DisableIdempotency`. `MapPost` is reserved for actual commands.
- Adopt `QUERY` once the API runs on .NET 11, Scalar and Schemathesis support OpenAPI 3.2 query operations, and Caddy and the web client are verified to handle the method end to end.
- Idempotency stays opt-out rather than opt-in, so a forgotten annotation costs a cache entry rather than a duplicated write.

Consequences:

- The command/query split becomes visible in routing: `MapQuery` marks which `POST` routes are reads.
- Query endpoints no longer require an idempotency key or get cached; idempotency for commands is unchanged.
- Migrating to `QUERY` later means changing one helper instead of every call site.
- Until then, the API uses a method that misdescribes these endpoints; OpenAPI consumers can't tell a search from a write by its method, and intermediaries can't cache search results.

Alternatives Considered:

- Adopt `QUERY` now on .NET 10: rejected, because the endpoints would vanish from OpenAPI, the documentation UI and contract tests.
- Attach `DisableIdempotency` at each call site: rejected, because nothing records why and a new search endpoint can easily forget it.
- Make idempotency opt-in: rejected, because a forgotten annotation would then duplicate a write instead of wasting a cache entry.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A016: Idempotency — Idempotency-Key Middleware

Status: Accepted

Context:

- Clients retry requests after timeouts and network errors. Without protection, a retried command can execute twice and duplicate a write.

Decision:

- Use an idempotency middleware in `Kernel.Idempotency` that covers `POST` and `PATCH` by default and requires an `Idempotency-Key` header on them; `PUT` and `DELETE` are idempotent by HTTP semantics and pass through. Endpoints opt out with `DisableIdempotency` ([ADR-A015](#adr-a015-read-only-endpoints-with-a-request-body--post-now-query-later)).
- Keys are scoped to the authenticated user and the route. Anonymous requests (applicants, [ADD §2](ADD.md#2-purpose)) are scoped to the route only, so their keys must be random UUIDs that can't be guessed. The successful response is stored in PostgreSQL (`kernel.idempotency`) for a fixed TTL and replayed for a repeated key instead of executing the command again.
- A repeated key with a different request body is rejected with `422`; a repeated key while the first request is still running is rejected with `409`.

Consequences:

- Coverage is opt-out, so a forgotten annotation costs a cache entry rather than a duplicated write.
- Clients must generate a new key per logical operation and reuse it only when retrying that operation.
- Claims survive restarts and are shared between instances; a claim stranded by a crash is taken over after a short lease ([idempotency.md](../api/idempotency/idempotency.md)).
- Every covered write costs a database round trip, and a daily job prunes expired records; response size and TTL are bounded.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-A017: Error Codes — a Typed Catalog, Declared per Endpoint, Generated into OpenAPI

Status: Accepted

Context:

- The frontend translates API errors into SK, CZ and EN ([ADR-F011](ADR-FRONTEND.md#adr-f011-localization--react-i18next-for-sk-cz-and-en)), but `detail` is English and includes ids, so it can't be a translation key.

Decision:

- Every problem response carries a stable `code` from a typed catalog (`ErrorCode`, `FieldErrorCode` in `Kernel.Errors`); handlers throw `Kernel.Exceptions`, endpoints declare their codes with `ProducesErrors`, and the OpenAPI document lists them ([errors.md](../api/errors/errors.md)).

Consequences:

- Clients translate on `code` and can generate their translation keys from the document.
- Codes are part of the API contract; renaming one after the first release is a breaking change.
- Command validation answers per-field codes; the framework's parameter validation answers `request.validation_failed` without them.

Dates & Owner:

- Decided: 13. 10. 2026
- Owner: Marek Dvorsky

---
