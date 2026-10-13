# Database model documentation

This directory contains the human-readable reference for HealthyHires's relational database model. The
DDL remains the source of truth for the exact current shape; these documents describe the rules,
ownership, relationships, and design decisions that are not obvious from raw SQL.

**Status:** Living documentation — update it with every schema migration.  
**Schema version:** `0.1.1`
**Database:** PostgreSQL
**Owners:** @data

> **How to use this documentation.** [Conventions](conventions.md) defines rules that apply to every
> table, so module entries only describe what is specific or deviates. To add a table, copy the
> [table template](#table-template). To record a design decision, add an entry to the
> [decision log](decision-log.md). Every schema change should land together with its documentation
> change in the same pull request.

## Index of files

- Conventions: [conventions.md](conventions.md) — rules that apply to every table, so module entries only describe what is specific or deviates.
- Decision log: [decision-log.md](decision-log.md) — design decisions that are not obvious from the DDL, with links to the relevant ADRs.
- Modules: [modules/README.md](modules/README.md) — each module owns a separate schema or bounded context. Table documentation is grouped by module.
- Cross-module references: [cross-module-references.md](cross-module-references.md) — rules for referencing tables across modules.
- Change log: [changelog.md](changelog.md) — a chronological list of schema changes, with links to the relevant migration scripts and PRs.

## Purpose & scope

This document is the human-readable reference for the application's relational model. It
describes every **module** (a logical grouping of tables that maps to a bounded context /
candidate microservice), every **table** within it, and the **conventions** that govern them.

It is the source of truth for the rules a reader
can't infer from raw DDL. The DDL itself remains the source of truth for the
*exact* current shape. Where the two disagree, treat it as a bug in this document and fix it.

## Notation & legend

**Column "Key" flags**

| Flag  | Meaning                                                                                     |
| ----- | ------------------------------------------------------------------------------------------- |
| `PK`  | Primary key                                                                                 |
| `FK`  | Foreign key (real, in-module)                                                               |
| `LFK` | Logical foreign key (cross-module, no DB constraint — see the cross-module reference rules) |
| `UQ`  | Has a unique constraint / unique index                                                      |
| `PFK` | Part of a composite PK **and** a foreign key (junction tables)                              |

- The **Null** column is `NO` / `YES`.
- `Audit: standard` means it carries the audit-column conventions block (rows omitted from the list).
- `Audit: lean` means it carries only `created_at` + `created_by` (the lean-link-table rules).

---

## Templates

### Module template

```markdown
## <ModuleName>

**Service / owner:** <which service owns this module>
**Purpose:** <1–3 sentences: what this bounded context is responsible for.>

**Tables**

| Table     | Purpose | Status     |
| --------- | ------- | ---------- |
| `table_a` | …       | Documented |
| `table_b` | …       | Template   |
```

### Table template

```markdown
### `table_name`

> One-sentence purpose.

**Audit:** standard | lean | none   ·   **Soft delete:** no | yes

**Columns**

| Column | Type   | Key | Null | Default    | Description |
| ------ | ------ | --- | ---- | ---------- | ----------- |
| `id`   | `UUID` | PK  | NO   | `uuidv7()` | …           |
| `…`    | `…`    |     |      |            | …           |

**Foreign keys**

| Column               | References                     | On delete  | Notes              |
| -------------------- | ------------------------------ | ---------- | ------------------ |
| `…_id`               | `schema.table(col)`            | `RESTRICT` | …                  |
| `job_offer_id`       | `recruitment.job_offer(id)`    | —          | logical — no DB FK |

**Constraints**

| Name      | Type  | Definition | Purpose |
| --------- | ----- | ---------- | ------- |
| `…_valid` | CHECK | `…`        | …       |

**Indexes**

| Name    | Columns | Type                           | Purpose |
| ------- | ------- | ------------------------------ | ------- |
| `idx_…` | `(…)`   | btree / unique / partial / gin | …       |

**Triggers**

| Name              | Timing        | Action                                                       |
| ----------------- | ------------- | ------------------------------------------------------------ |
| `trg_…_set_audit` | BEFORE UPDATE | sets `updated_at`, bumps `version` (the audit-trigger rules) |

**Notes**

- …
```

---

## Maintenance

The visual schema of the data model is maintained as a diagram in
[DBModel.drawio](./DBModel.drawio). Keep it in sync with this document and the ddl script.

The drawio diagram carries a **doc version number** label matching this document's
**Schema version** — it records which version of this document the drawio model represents.

**Process suggestions**

- Bump the **Schema version** and add a **Changelog** row in the same migration PR.
- Record any non-obvious modelling choice as a new **ADR** [decision log](decision-log.md).
- Treat any mismatch between this document, ddl and drawio model as a doc bug.

<!-- markdownlint-disable-file MD036 -->
