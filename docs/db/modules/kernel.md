# Kernel (shared)

**Service / owner:** the API host itself
**Purpose:** infrastructure the host owns rather than any module — the HTTP idempotency records taken by
middleware before routing has chosen a handler. They cannot live in a module's schema, because they do not
belong to a module — see [ADR-D001](../decision-log.md#adr-d001--idempotency-records-get-a-shared-kernel-schema-not-a-modules).

**Tables**

| Table | Purpose | Status |
| ----- | ------- | ------ |
| [`idempotency`](#idempotency) | One claimed or completed HTTP `Idempotency-Key`, with its captured response | **Documented** |

The DDL is in `configs/postgres/init/01-create-db.sql` ([ADR-D002](../decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production));
`IdempotencyDbContext` maps the table with `ExcludeFromMigrations`.

## `idempotency`

> One row per claimed idempotency key. `completed = false` is a claim in flight; `true` carries the
> captured response, replayed verbatim until `expires_at`.

**Audit:** none · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `key` | `TEXT` | PK | NO | | Fully-scoped key — actor + method + path + the client's key, hashed |
| `fingerprint` | `BYTEA` | | NO | | SHA-256 over method + path + query + body, so key reuse with a different payload is detectable |
| `claim_owner` | `UUID` | | NO | | Which request holds the claim — separates a retry of the holder's own attempt from a genuine duplicate |
| `completed` | `BOOLEAN` | | NO | `false` | Whether the response has been captured and is replayable |
| `lease_expires_at` | `TIMESTAMPTZ` | | NO | | While uncompleted: when another request may take this claim over |
| `expires_at` | `TIMESTAMPTZ` | | NO | | When the record stops being replayable and may be pruned |
| `status_code` | `INTEGER` | | YES | | Captured status code — `null` while uncompleted |
| `content_type` | `TEXT` | | YES | | Captured `Content-Type` |
| `location` | `TEXT` | | YES | | Captured `Location` header |
| `body` | `BYTEA` | | YES | | Captured response body |
| `created_at` | `TIMESTAMPTZ` | | NO | `now()` | When the key was first claimed |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| `PK_idempotency` | `(key)` | btree | The claim upsert and every lookup |
| `idx_idempotency_expires_at` | `(expires_at)` | btree | Age-based range scan for the retention pruner |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `idempotency_completed_has_status` | CHECK | `completed = false OR status_code IS NOT NULL` | A completed record must carry something to replay |

**Notes**

- **Claiming is one statement.** An `INSERT … ON CONFLICT DO UPDATE … WHERE` covers all three ways a key
  becomes claimable: first sight, a record past `expires_at`, and a claim past `lease_expires_at`. Expiry
  is compared against the database's `now()`, so instances with drifting clocks still agree.
- **The lease guard carries a fingerprint check.** A takeover overwrites the stored fingerprint, so it is
  only granted to a request whose payload matches; a different payload still gets `422`.
- **`claim_owner` makes a retried claim safe.** The context retries on transient failures, so an `INSERT`
  that committed but whose acknowledgement was lost is re-sent. Matching the owner is what stops the store
  answering that retry with a conflict against the request that holds the claim. The value comes from the
  scoped store instance, so it is per request by construction.
- **Completion and release are owner-scoped too.** Both statements carry `AND claim_owner = …`, so they act
  only on a claim the calling request still holds. The case that needs it is a request slow enough to lose
  its lease: it is still running and will still finish, but by then the key belongs to whoever took it over.
  Without the guard its completion would overwrite the winner's captured response with the response of the
  attempt that lost the key, and its release would delete a live claim outright — leaving the key open for a
  third request to start alongside the second.
- **Uses PostgreSQL 18.** The claim reads `old.completed` in `RETURNING` to tell a lease takeover from a
  normal expiry, which needs old-row values in `RETURNING` (PostgreSQL 18+).
- **No audit trigger.** The rows are ephemeral transport state, not business data; claim outcomes go to
  metrics and logs.
- **Bounded by retention.** `IdempotencyPruner` ([daily](../../api/scheduling/scheduling.md), 03:00 UTC) deletes records past `expires_at`, except
  unfinished claims whose lease is still live. The table is churn-heavy, hence its own autovacuum settings —
  see [ADR-D001](../decision-log.md#adr-d001--idempotency-records-get-a-shared-kernel-schema-not-a-modules).
