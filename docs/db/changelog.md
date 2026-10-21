# Changelog

- **2026-10-12 · schema 0.1.0** — Initial documentation: conventions, templates.
- **2026-10-13 · schema 0.1.1** — Added the `kernel` schema (host-owned) with `kernel.idempotency`: HTTP `Idempotency-Key` claims and captured responses, `idx_idempotency_expires_at` for the daily `IdempotencyPruner` (03:00 UTC), the `idempotency_completed_has_status` CHECK and churn autovacuum settings — see ADR-D001. Landed in `configs/postgres/init/01-create-db.sql`, now loaded by the AppHost — see ADR-D002.
- **2026-10-21 · schema 0.2.0** — Added the `recruitment` schema with `recruitment.job_post` (title, location, department, draft/published, open positions, payment type, employment type, the responsible recruiter as an Entra object id) and its `recruitment.set_audit_fields()` audit trigger — see ADR-D003 and ADR-D004. Schema listed in `infra/db-schemas.txt`.
