# Documentation Index

Index of the HealthyHires documentation. HealthyHires is a talent acquisition web application for a single company: anonymous applicants browse job offers and apply, and staff sign in with Microsoft Entra ID to manage offers and applications.

New to the docs? Start with the [Architecture Description Document](architecture/ADD.md), and read [How to Write Docs](how-to-write-docs.md) before editing anything.

## Architecture

- [Architecture Description Document](architecture/ADD.md) — overview: purpose, scope, stakeholders, viewpoints, constraints and quality attributes.
- [System Context View](architecture/system-context.md) — actors, external systems, trust boundaries and data flows.
- [Container / Deployment Architecture](architecture/container-deployment.md) — what runs on the Azure VM, ports, networking, deployment and rollback.
- [Identity & Access Management](architecture/iam.md) — staff sign-in with Entra ID, anonymous applicants, managed identity, secrets inventory, CI and operator access.
- [Contract Testing](architecture/contract-testing.md) — Schemathesis against the OpenAPI document.
- [Development Architecture](architecture/development-architecture.md) — repository layout, .NET build and central packages, local development and tests.
- [Infrastructure / Operations Architecture](architecture/infrastructure-operations-architecture.md) — environments, provisioning with Bicep, host lifecycle, patching, drift and ownership.
- [Observability Architecture](architecture/observability-architecture.md) — telemetry pipeline, Azure Monitor tables and retention, health checks, measuring the targets, alerts and audit trails.
- [Aspire Development Stack](architecture/aspire-dev-stack.md) — the Aspire AppHost's local backend: resources, credentials, start order, lifecycle and troubleshooting.

## Frontend

- [Getting Started with the Frontend](app/getting-started.md) — prerequisites, install, the dev server and its API proxy, production builds, lint and troubleshooting.

## Operations

- [Azure Deployment Guide](operations/azure-deployment-guide.md) — step by step from an empty subscription to a running deployment, day-to-day operation, and moving to a teammate's subscription.

## API

- [First Module Tests](api/first-module-tests.md) — the tests that check every module endpoint, to be added with the first module.
- [Implementation Guide](api/implementation-guide.md) — the modular monolith, how a module is laid out, how to create a module and a feature, validation, the decorator chain and the backend conventions.
- [Error Model](api/errors/errors.md) — problem responses, the exception taxonomy, validation errors, error codes and how they reach the OpenAPI document.
- [Idempotency](api/idempotency/idempotency.md) — the `Idempotency-Key` contract, defaults, per-endpoint overrides, claim lifecycle and limitations.
- [Scheduling](api/scheduling/scheduling.md) — daily and periodic background workers: writing, registering, behaviour and the current schedule.

## Database

- [Database model documentation](db/README.md) — conventions, modules and tables, decision log and changelog.

## Architecture Decisions

- [Architecture Decision Records](architecture/ADR.md) — platform and infrastructure decisions.
- [ADR — ASP.NET](architecture/ADR-ASPNET.md) — backend (modular monolith) decisions.
- [ADR — Frontend](architecture/ADR-FRONTEND.md) — React web application decisions.

## Diagrams

PlantUML sources and rendered SVGs live in [diagrams/architecture](diagrams/architecture); re-render them with `scripts/render-diagrams.sh`.

- [C1 System Context](diagrams/architecture/C1_System_Context.svg)
- [C1 Supply Chain](diagrams/architecture/C1_Supply_Chain.svg)
- [C2 Containers](diagrams/architecture/C2_Containers.svg)
- [C2 VM Containers](diagrams/architecture/C2_vm_containers.svg)
- [C3 Telemetry Pipeline](diagrams/architecture/C3_telemetry_pipeline.svg)

## Contributing

- [How to Write Docs](how-to-write-docs.md) — Markdown conventions, diagrams, VS Code, markdownlint and table compression.

## Planned

These documents are referenced from the architecture docs but not written yet:

- `architecture/threat-model.md` — threats and mitigations per trust boundary.
- `architecture/data-architecture.md` — modules, schemas, blob containers and main entities.
- `architecture/backup-recovery-plan.md` — RPO/RTO, restore procedures and drills.
