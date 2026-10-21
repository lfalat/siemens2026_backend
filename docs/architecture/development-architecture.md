# Development Architecture

Describes the repository layout, the .NET build setup and local-development tooling.

## 1. Repository Layout

HealthyHires is a **single repository** that holds the source code, the documentation and the local-development assets:

| Path | Contents |
| ---- | -------- |
| `Api/` | Backend: ASP.NET modular monolith (C#) with its own `Api.slnx` ([ADR-A001](ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith)) |
| `App/` | Frontend: React, TypeScript and Vite single-page application ([ADR-F001](ADR-FRONTEND.md#adr-f001-application-platform--react-typescript-and-vite)) |
| `AppHost/` | Aspire AppHost that runs the backend and its local dependencies |
| `configs/` | Configuration for local development, e.g. the local Caddyfile in `configs/caddy/` |
| `configs_production/` | Configuration deployed to the VM: `Caddyfile.production` and `site.caddy`, the routes and headers the local Caddyfile imports too |
| `docs/` | Architecture, security and operations documentation |
| `scripts/` | Repository tooling, e.g. diagram rendering |
| `healthyhires.slnx` | Root .NET solution: the AppHost plus every project under `Api/` |
| `Directory.Build.props`, `Directory.Packages.props` | Build settings and package versions for the root projects (AppHost) |
| `aspire.config.json` | Points the Aspire CLI at `AppHost/AppHost.csproj` |
| `infra/` | Bicep templates and cloud-init for Azure ([ADR-023](ADR.md#adr-023-infrastructure-as-code--bicep), [infrastructure-operations-architecture.md](infrastructure-operations-architecture.md)) |
| `deploy/` | What runs on the VM: Quadlet units, Collector configuration, Falco rules, the Caddy image and the host scripts |
| `scripts/azure/` | Operator scripts: provisioning, NetBird enrolment, the database grant, migration between subscriptions |
| `.github/workflows/` | `ci.yml` for pull requests; `deploy.yml` builds, scans, signs and deploys from `main` |
| `.trivyignore`, `.zap/rules.tsv` | The only places a Trivy or ZAP finding can be accepted, each with its reason ([ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign), [ADR-020](ADR.md#adr-020-dynamic-application-security-testing--owasp-zap)) |
| `Justfile` | [Just](https://github.com/casey/just) recipes that wrap the local-development commands in §3 and the Azure scripts |
| `README.md` | Quick start: how to run the backend, the frontend and the local edge |

### 1.1 Backend (`Api/`)

```text
Api/
├── Api.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── ApiService/     # ASP.NET host: Program.cs, endpoint mapping, configuration
├── Modules/        # one class library per business module
├── Shared/         # contracts and cross-cutting code shared by modules
│   ├── Kernel/           # shared kernel every module references: log data classification, JSON serialization
│   └── ServiceDefaults/  # telemetry, health checks, service discovery, HttpClient resilience
└── Tests/          # unit and integration tests
```

`ApiService` is the only deployable project; it references the modules and maps their endpoints. Modules never reference each other, only `Shared/` contracts ([ADR-A001](ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith)).

`ServiceDefaults` is the Aspire service-defaults project: `ApiService` calls `AddServiceDefaults()` and `MapDefaultEndpoints()` from its `Extensions.cs`, which add:

- OpenTelemetry logs, metrics and traces, exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set: to the Aspire dashboard locally, to the Collector on the VM ([ADR-A009](ADR-ASPNET.md#adr-a009-telemetry--opentelemetry-with-ef-core-integration), [ADR-007.1](ADR.md#adr-0071-telemetry-pipeline--opentelemetry-collector-otelcol-contrib)).
- The `/health` and `/alive` endpoints, mapped in every environment and returning only the aggregate status; in production only `/health` is routed through Caddy ([container-deployment.md §1.3](container-deployment.md#13-aspnet-api-healthyhires-api)).
- Service discovery and the standard resilience handler for every `HttpClient` ([ADR-A011](ADR-ASPNET.md#adr-a011-resilience--microsoftextensionsresilience)).

`Kernel` is the shared kernel that modules may reference. It holds `UtcDateTimeOffsetConverter`, which reads every timestamp in a request as UTC so Npgsql can bind it to a `timestamptz` column, and the data-classification attributes for logging: `[PersonalData]` for anything that identifies a person (an applicant's name, e-mail or phone number) and `[SensitiveData]` for credentials and caller-controlled content (tokens, SAS URLs, cover-letter text, file names).

**Logging rule:** every log call is a `[LoggerMessage]` method, and every argument that carries personal or sensitive data is marked with one of those attributes. `ApiService` registers erasing redactors for both classifications, and for any classification without its own redactor, so a marked value never reaches the console, the journal or OTLP ([ADR-A009](ADR-ASPNET.md#adr-a009-telemetry--opentelemetry-with-ef-core-integration)). `Api/Directory.Build.props` turns the three ways around this into build errors: a direct `LogInformation(...)` call (`CA1848`), a template that varies between calls such as an interpolated string (`CA2254`), and an argument evaluated before the level check (`CA1873`). Redaction does not reach logging scopes or span attributes, so personal data never goes there.

### 1.2 Frontend (`App/`)

The frontend is a separate npm project. Its `src/` structure (`app/`, `shared/`, `features/<module>/`) mirrors the backend modules, as described in [ADR-F002](ADR-FRONTEND.md#adr-f002-application-architecture--feature-first-mirroring-backend-modules) and `App/README.md`.

## 2. .NET Build

Both levels of the repository use **Central Package Management**: each `Directory.Packages.props` lists package versions once, and project files reference packages without a version.

| File | Applies to |
| ---- | ---------- |
| `Directory.Build.props`, `Directory.Packages.props` (root) | `AppHost/` |
| `Api/Directory.Build.props`, `Api/Directory.Packages.props` | Every project under `Api/` |

MSBuild uses the nearest `Directory.*.props` file, so `Api/` is self-contained: `Api/Api.slnx` restores and builds without the root files, and the root solution builds the API with the API's own versions. The two `Directory.Build.props` files share the same settings:

- `net10.0`, nullable reference types, implicit usings, latest C# version.
- `UseArtifactsOutput`: build output goes to `artifacts/` (root) and `Api/artifacts/` instead of `bin/` and `obj/` in each project.
- `CentralPackageTransitivePinning` and `NuGetAuditMode=all`: transitive packages are audited too, and a vulnerable one is pinned to a fixed version in `Directory.Packages.props`.
- Code style is enforced during the build with the `latest-recommended` analyzer set.

Build either solution from the repository root:

```bash
dotnet build healthyhires.slnx
```

```bash
dotnet build Api/Api.slnx
```

## 3. Local Development

Every command in this section has a recipe in the root `Justfile` (`just aspire-up`, `just app`, `just app-build`, `just edge`); `just` lists them all. The recipes only wrap these commands, so `just` is optional.

### 3.1 Aspire: backend with its local dependencies

The Aspire AppHost (`AppHost/`) starts the API together with its local dependencies with one command and opens the Aspire dashboard with logs, traces and resource health. It needs Docker or Podman and no secrets. It runs the backend only: the frontend is started on its own (§3.2), so neither side's run depends on the other.

```bash
dotnet run --project AppHost
```

| Resource | Image | Stands in for | Host port | Development credentials |
| -------- | ----- | ------------- | --------- | ----------------------- |
| `postgres` (database `healthyhires`) | `postgres:18` | Azure Database for PostgreSQL ([ADR-006](ADR.md#adr-006-primary-database--postgresql-managed)) | 5432 | `healthyhires` / `healthyhires-dev` |
| `storage` → `blobs` | Azurite | Azure Blob Storage ([ADR-005](ADR.md#adr-005-object-storage--azure-blob-storage)) | 10000 (blob) | Azurite's well-known development account |
| `mailpit` | Mailpit | ACS Email ([ADR-010](ADR.md#adr-010-email-delivery--azure-communication-services-email)) | 1025 (SMTP), 8025 (web inbox) | none |
| `clamav` | `clamav/clamav:1.5.4` | the same ClamAV container as on the VM ([container-deployment.md §1.4](container-deployment.md#14-clamav-healthyhires-clamav)) | 3310 (clamd) | none |
| `apiservice` | — | the API | 5180 (HTTP), 7180 (HTTPS) | — |

- Ports and credentials are fixed, so local tools connect with the same values on every run. They are development-only values set in `AppHost.cs`.
- The database and storage have no data volume, so they start empty on every run. ClamAV is the exception: its signatures live in the named volume `healthyhires-clamav-db`, because a fresh container downloads several hundred MB and ClamAV's mirror rate-limits repeated downloads. Remove the volume to start from the signatures bundled in the image.
- The API gets connection strings for `healthyhires`, `blobs` and `mailpit` from the AppHost and starts only when all three are ready. It also gets the clamd address as `services__clamav__tcp__0` for service discovery, but does not wait for ClamAV: as on the VM, uploads stay in the "scanning" state until ClamAV is ready.
- ClamAV's health check sends clamd a `PING`, so the dashboard reports it healthy only once its signatures are loaded and it accepts scans.
- `/health` passes only when the API reaches PostgreSQL and Blob Storage; ClamAV is left out, because the application stays available while it is down; `/alive` checks only that the process responds. The AppHost uses `/health` to report the API's state.
- Mailpit catches every message the API sends locally and shows it in its web inbox at `http://localhost:8025`; nothing is delivered.

This is the fastest way to iterate on the backend. The dashboard links to the API's Scalar reference at `/scalar`. The resources, credentials, start order and troubleshooting are described in [Aspire Development Stack](aspire-dev-stack.md).

### 3.2 Frontend

The frontend runs on its own with the Vite dev server, which reloads on every change ([ADR-F015](ADR-FRONTEND.md#adr-f015-local-development--vite-dev-server-with-an-api-proxy)). It needs Node and `npm ci` in `App/` once, and nothing from the backend toolchain. Step-by-step setup, configuration and troubleshooting are in [Getting Started with the Frontend](../app/getting-started.md):

```bash
npm run dev --prefix App
```

- In production Caddy serves the app and the API on one origin ([container-deployment.md §1.2](container-deployment.md#12-caddy-healthyhires-caddy)). The dev server keeps that true locally by proxying `/api` and `/hubs` (SignalR, WebSockets included) to the API, so the browser only talks to the dev server, the frontend calls relative paths (`/api/v1/...`) in every environment, and the API needs no CORS policy.
- The proxy target is `API_URL`, default `http://localhost:5180`: the API's HTTP port both under the AppHost and when `ApiService` runs on its own. Set it in `App/.env` (see `App/.env.example`) or the environment to point elsewhere. It has no `VITE_` prefix, so it stays in `vite.config.ts` and never reaches the bundle.
- Without a running API, calls to `/api` fail with a proxy error. Mock Service Worker handlers ([ADR-F012](ADR-FRONTEND.md#adr-f012-testing--vitest-react-testing-library-and-playwright)) will let the frontend run without the backend once the first features use the API.
- The dev server does not apply the production Content Security Policy: React's hot reload needs an inline script that `script-src 'self'` forbids. Check the CSP against a build, through the local edge (§3.3).

### 3.3 Production-like edge (optional)

Not needed for everyday work. To see the application as the VM serves it — one origin, the production headers and Content Security Policy, the cache rules — or to run ZAP, Playwright or k6 against it, start Caddy with `configs/caddy/Caddyfile`. It imports the production routes and headers from `configs_production/site.caddy`, so there is a single copy of them, and listens on `http://localhost:8088`.

Build the frontend, start the API (through the AppHost or on its own), then from the repository root:

```bash
npm run build --prefix App
```

```bash
docker run --rm --name healthyhires-edge --network host -v "$PWD/configs/caddy/Caddyfile:/etc/caddy/Caddyfile:ro" -v "$PWD/configs_production/site.caddy:/etc/caddy/site.caddy:ro" -v "$PWD/App/dist:/srv:ro" -e API_UPSTREAM=localhost:5180 -e BLOB_ENDPOINT=http://localhost:10000 caddy:2.11.7
```

The two Caddyfiles are mounted individually, so both land in `/etc/caddy` and `import site.caddy` resolves next to the local Caddyfile, as it does on the VM.

Host networking lets Caddy reach the API on `localhost`, as it does inside the pod on the VM. It is native on Linux; Docker Desktop on Windows and macOS needs host networking enabled in its settings.

## 4. Tests

- **xUnit v3** unit and integration tests live in `Api/Tests/`, one project mirroring the source layout (`Tests/ApiService/`, `Tests/Shared/`). They run on Microsoft.Testing.Platform, selected by `global.json`, with `dotnet test --solution Api/Api.slnx` or from the root solution. Integration tests start real dependencies with Testcontainers, so they need Docker or Podman: PostgreSQL now, Azurite with the file module ([ADR-A010](ADR-ASPNET.md#adr-a010-file-storage--azure-blob-storage-via-azure-sdk)). The PostgreSQL container is shared by the whole run and started only when a test needs it; it builds a template database from `configs/postgres/init/01-create-db.sql` (not the development seed), and each schema-backed test gets a fresh clone of it (`PostgresTestHost.CreateDatabase`). Fault-injection tests reach their clone through a Toxiproxy of their own and run serially.
- **Vitest** with React Testing Library and MSW cover frontend components and hooks ([ADR-F012](ADR-FRONTEND.md#adr-f012-testing--vitest-react-testing-library-and-playwright)).
- **Playwright** end-to-end tests, in Playwright for .NET on xUnit v3, live in `Tests/e2e/` and run against the local stack with `just e2e` ([ADR-019](ADR.md#adr-019-end-to-end-testing--playwright), [e2e.md](../testing/e2e.md)).
- **Schemathesis** contract tests run against the API's OpenAPI document ([ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis), [contract-testing.md](contract-testing.md)).
- **k6** load tests run on demand against the local stack, never against production ([ADR-018](ADR.md#adr-018-load-and-performance-testing--k6)).
- **ZAP** dynamic scans run through Caddy against the local stack ([ADR-020](ADR.md#adr-020-dynamic-application-security-testing--owasp-zap)).
- **Toxiproxy** fault injection runs in integration tests and optionally in the local stack ([ADR-021](ADR.md#adr-021-fault-injection--toxiproxy)).

## 5. Development Conventions

- Backend conventions are set in the ASP.NET ADRs ([ADR-ASPNET.md](ADR-ASPNET.md)); frontend conventions, including ESLint and Prettier, in [ADR-FRONTEND.md](ADR-FRONTEND.md).
- New packages get a version in the matching `Directory.Packages.props`, never in a project file.
- Build, signing and supply-chain requirements are shown in [C1 Supply Chain](../diagrams/architecture/C1_Supply_Chain.svg) and decided in [ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign).
