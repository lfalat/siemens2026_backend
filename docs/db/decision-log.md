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

---

## ADR-D003 — The audit trigger function lives in each module's schema

**Date:** 2026-10-21 · **Status:** accepted

The [audit trigger](conventions.md#audit-trigger-the-version--updated_at-rule) function is created as
`<module>.set_audit_fields()`, one copy per module schema, starting with `recruitment.set_audit_fields()`.

- A module's schema stays self-contained: extracting a module ([ADR-A001](../architecture/ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith))
  takes its tables and their trigger function along, with nothing left pointing at `public` or `kernel`.
- The function is five lines and never changes, so the duplication costs nothing in practice.

---

## ADR-D004 — A job post's recruiter is an Entra object id, checked by the application

**Date:** 2026-10-21 · **Status:** accepted

`recruitment.job_post.recruiter_id` holds the Entra object id (`oid`) of the responsible recruiter. Staff accounts
and their app roles live only in Entra ([iam.md §2.4](../architecture/iam.md#24-session-management)); there is no
staff table to reference.

- There is no foreign key and no role constraint. The assigning handler asks the directory (Microsoft Graph) that the
  user holds the `Recruiter` role at the time of assignment.
- A recruiter who later loses the role or is deleted in Entra stays on the job post until a hiring manager assigns
  someone else. Reacting to that would need Entra change notifications, which nothing needs yet.
- `created_by` and `updated_by` hold Entra object ids for the same reason, as every staff reference will.
