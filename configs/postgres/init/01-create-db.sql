-- ============================================================================
--  Database schema (DDL)
--  Generated from docs/db/README.md and its module documents  (schema version 0.1.x)
--
--  Target: PostgreSQL 18+  (uses old-row values in RETURNING, native uuidv7()).
--  Mounted into /docker-entrypoint-initdb.d by the Aspire AppHost. It runs once, against
--  the fresh database the container creates (POSTGRES_DB), so it does not CREATE DATABASE
--  itself. The local database has no volume, so every AppHost run starts from this script.
--
--  This script is the schema (ADR-D002 in docs/db/decision-log.md): edit it in place and
--  bump the documentation's schema version in the same change. The EF Core contexts map
--  their tables with ExcludeFromMigrations.
-- ============================================================================

-- ---------------------------------------------------------------------------
-- Schemas (one per module / bounded context, plus the host-owned kernel)
-- ---------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS kernel;


-- ===========================================================================
--  SHARED: kernel
-- ===========================================================================

-- idempotency  (HTTP Idempotency-Key records, ADR-A016 / idempotency middleware)
-- One row per claimed idempotency key. `completed = false` is a claim in flight; `true` carries the
-- captured response, replayed verbatim until `expires_at`.
--
-- A schema of its own rather than a module's: a claim is taken by host middleware *before* routing has
-- chosen a handler, so there is no module to attribute it to (ADR-D001).
--
-- `lease_expires_at` is what makes a durable store safe. A process that dies mid-request never completes
-- or abandons its claim; without a takeover window every retry of that key would be answered 409 until the
-- full TTL elapsed. The in-memory store needs no lease only because its state dies with the process that
-- stranded it. The lease must exceed the slowest covered endpoint — if it expires while the work is still
-- running, a duplicate gets in.
--
-- `claim_owner` is what distinguishes "my own attempt, retried" from "somebody else's claim". The API's
-- contexts retry on transient failures, so a claim whose INSERT committed but whose acknowledgement was
-- lost gets re-sent; without an owner the store would answer that retry with 409 — telling a request that
-- the claim it owns belongs to someone else. The owner is per request (the store is scoped), so any
-- second look at a claim carrying the same owner is the same attempt.
--
-- It guards completion and release as well, not just the claim. A request slow enough to lose its lease is
-- still running and will still finish, but the key is no longer its own: completing would overwrite the
-- winner's captured response with the loser's, and releasing would delete a live claim and let a third
-- request start alongside the second.
--
-- Churn-heavy by design: insert, one update, delete within the TTL, at the rate of covered writes. That
-- produces dead tuples far faster than the row count suggests, hence the per-table autovacuum settings.
CREATE TABLE kernel.idempotency (
    key              TEXT         PRIMARY KEY,
    fingerprint      BYTEA        NOT NULL,  -- SHA-256 over method + path + query + body
    claim_owner      UUID         NOT NULL,  -- which request holds the claim; see the note on retries above
    completed        BOOLEAN      NOT NULL DEFAULT false,
    lease_expires_at TIMESTAMPTZ  NOT NULL,  -- while uncompleted: when another request may take the claim over
    expires_at       TIMESTAMPTZ  NOT NULL,  -- when the record stops being replayable and may be pruned
    status_code      INTEGER,                -- captured response, null while uncompleted
    content_type     TEXT,
    location         TEXT,
    body             BYTEA,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT idempotency_completed_has_status CHECK (completed = false OR status_code IS NOT NULL)
);
-- Makes the retention pass a cheap range delete instead of a full-table scan.
CREATE INDEX idx_idempotency_expires_at ON kernel.idempotency (expires_at);
-- The default 20% dead-tuple threshold lets a churn table like this bloat between passes.
ALTER TABLE kernel.idempotency SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.02);
