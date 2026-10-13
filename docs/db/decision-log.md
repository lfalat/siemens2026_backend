# Decision log (ADR-lite)

> Append-only. Each entry: date, decision, and the reasoning. Never edit past entries —
> supersede them with a new one.

---

## ADR-D001 — Idempotency records get a shared `kernel` schema, not a module's

**Date:** 2026-10-13 · **Status:** accepted

`kernel.idempotency` holds the HTTP `Idempotency-Key` records ([ADR-A016](../architecture/ADR-ASPNET.md#adr-a016-idempotency--idempotency-key-middleware)).
A claim is taken by host middleware *before* routing has selected a handler, so there is no module to attribute
it to, and putting it in one would make that module look like its owner.

The record therefore cannot be atomic with the write it protects, and does not try to be. It closes the restart
and multi-instance windows (a claim outlives the process that took it), leaving a smaller one: a crash between
the endpoint's commit and the response being captured. Closing that needs a natural key on the business row,
decided per endpoint.

Two consequences the table's shape encodes:

- **`lease_expires_at`.** A process that dies mid-request never completes or abandons its claim. Without a
  takeover window every retry of that key would get `409` until the full TTL elapses. The lease must exceed the
  slowest covered endpoint, or a duplicate gets in while the work is still running.
- **Autovacuum.** Rows are inserted, updated once and deleted within the TTL, so dead tuples pile up far faster
  than the row count suggests. The table carries `autovacuum_vacuum_scale_factor = 0.02` rather than the 20%
  default, set in the DDL.

---

## ADR-D002 — The schema is an init DDL script until production

**Date:** 2026-10-13 · **Status:** accepted

`configs/postgres/init/01-create-db.sql` is the schema. The AppHost loads it into a fresh local database on every
run, and it is edited in place; the EF Core contexts map their tables with `ExcludeFromMigrations`. This narrows
[ADR-A005](../architecture/ADR-ASPNET.md#adr-a005-persistence--postgresql-with-entity-framework-core) for the
pre-production phase.

- There is no data to migrate yet, so `ALTER` steps would only record churn; one script reads as the whole model.
- The DDL can say what EF Core's model cannot (storage parameters, triggers, partial indexes) without hand-edited migrations.
- Before the first production deployment, this script becomes the baseline and later changes need migrations — a
  decision still to be made.
