# Aspire Development Stack

How `just aspire-up` (`dotnet run --project AppHost`) brings up a complete, self-contained HealthyHires backend on a machine that has nothing but Docker (or Podman) and the .NET SDK.

The defining property of this stack is that **it needs no secrets and no access to any Azure service**.
A fresh clone runs. Every Azure service the VM uses has a local stand-in, started with fixed development values and thrown away on exit.

Source of truth for this document:

- `AppHost/AppHost.cs` - the resource graph
- `Api/ApiService/Program.cs` - how the API consumes it
- `Api/ApiService/Properties/launchSettings.json` - the API's ports

The stack runs the **backend only**. The frontend runs on its own against the API's URL ([Getting Started with the Frontend](../app/getting-started.md)), and the production-like Caddy edge is started on demand; [development-architecture.md §3](development-architecture.md#3-local-development) covers how the three fit together.

---

## 1. Resources

```
postgres ─────── database healthyhires (one schema per module)
storage ──────── Azurite → blobs (one container per module)
mailpit ──────── catches every e-mail the API sends
clamav ───────── clamd; signatures in a named volume            not waited for
   │
apiservice ───── starts once postgres, blobs and mailpit are healthy
```

| Resource | Where it listens | Stands in for | Notes |
| -------- | ---------------- | ------------- | ----- |
| `postgres` | `localhost:5432` | Azure Database for PostgreSQL ([ADR-006](ADR.md#adr-006-primary-database--postgresql-managed)) | Image `postgres:18`; no data volume — see §6 |
| `storage` → `blobs` | `http://localhost:10000` | Azure Blob Storage ([ADR-005](ADR.md#adr-005-object-storage--azure-blob-storage)) | Azurite, the Aspire integration's image |
| `mailpit` | `http://localhost:8025` (inbox), `:1025` (SMTP) | ACS Email ([ADR-010](ADR.md#adr-010-email-delivery--azure-communication-services-email)) | Nothing ever leaves the machine |
| `clamav` | `localhost:3310` (clamd) | the ClamAV container on the VM ([container-deployment.md §1.4](container-deployment.md#14-clamav-healthyhires-clamav)) | Image `clamav/clamav:1.5.4`, the same as on the VM — see §4 |
| `apiservice` | `http://localhost:5180`; `https://localhost:7180` with the `https` launch profile | the API | `/scalar` for API docs, linked from the dashboard |
| Aspire dashboard | `https://localhost:17180` (`http://localhost:15180` with the `http` profile) | the OpenTelemetry Collector and Azure Monitor | Logs, traces, metrics and resource health |

Every port is **pinned**, not assigned by Aspire. The frontend's dev server proxies to `http://localhost:5180` and the local edge to `localhost:5180` and `localhost:10000`, and local tools (psql, DBeaver, Azure Storage Explorer) connect with the same values on every run. A fixed port that is already taken fails the resource at start — see §7.

## 2. Credentials

Everything below is a fixed development value and is **deliberately committed** in `AppHost.cs`. None of it is a secret: the instance it unlocks is destroyed on every restart.

| What | Value |
| ---- | ----- |
| PostgreSQL | `healthyhires` / `healthyhires-dev`, database `healthyhires` |
| Azurite | Azurite's well-known development account (`devstoreaccount1`) |
| Mailpit inbox | `http://localhost:8025` — no login |
| ClamAV | none |
| Aspire dashboard | Opened with a one-time login token in the URL; the token is also printed in the AppHost's console |

The PostgreSQL credentials are AppHost parameters (`postgres-username`, `postgres-password`) with these values as defaults, so a value in the AppHost's user secrets overrides them without changing the file.

## 3. How the API consumes the stack

The API never reads an AppHost-specific setting. Aspire hands it ordinary configuration, the same keys production fills from Quadlet environment variables and Key Vault ([container-deployment.md](container-deployment.md)):

| Configuration key | From | Read by |
| ----------------- | ---- | ------- |
| `ConnectionStrings__healthyhires` | `postgres` → `healthyhires` | `AddNpgsqlDataSource("healthyhires")` |
| `ConnectionStrings__blobs` | `storage` → `blobs` | `AddAzureBlobServiceClient("blobs")` |
| `ConnectionStrings__mailpit` | `mailpit` | nothing yet — reserved for the e-mail feature |
| `services__clamav__tcp__0` | `clamav`'s `tcp` endpoint, through service discovery | nothing yet — reserved for the upload scanner |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | the Aspire dashboard | `AddServiceDefaults()` |

**Start order.** `apiservice` waits for `postgres`, `blobs` and `mailpit`, so it never starts against a database that is not accepting connections. It deliberately does **not** wait for ClamAV: on the VM the API runs while ClamAV is down, and uploads stay in the "scanning" state until it is ready. The local stack keeps that behaviour rather than hiding it.

**Health.** The Npgsql and Blob Storage integrations each register a health check, so `/health` passes only when the API reaches both. ClamAV and Mailpit are left out on purpose, because the application stays available without them. `/alive` checks only that the process responds. The dashboard reports `apiservice` from `/health`.

**Development only.** `/openapi/v1.json` and the Scalar reference at `/scalar` are mapped only in the `Development` environment, which both launch profiles set.

## 4. ClamAV

ClamAV is the one resource that keeps data between runs: its signature database lives in the named volume `healthyhires-clamav-db`. A fresh container downloads several hundred MB of signatures, and ClamAV's mirror rate-limits clients that download them on every start.

Loading the signatures into memory still takes up to a minute after each start, and clamd opens its port before it is ready. A plain TCP check would therefore report it healthy too soon, so the AppHost registers its own health check that sends clamd `zPING` and waits for `PONG`. The dashboard shows ClamAV healthy only once it accepts scans.

`just clamav-reset` removes the volume (stop the AppHost first), so the next run starts from the signatures bundled in the image.

## 5. Sign-in

The stack runs **no identity provider**. Entra ID has no local emulator, and HealthyHires does not need one:

- Anonymous applicants never sign in, so their flows work with nothing extra.
- Staff sign in against the **real** Entra tenant, with `https://localhost:<port>` registered as an SPA redirect URI ([iam.md §8](iam.md#8-local-development)). The API validates tokens locally against Entra's published signing keys, so it needs the tenant and client ids but no secret.
- No Azure managed identity is used locally: the Azure services it would authenticate to are replaced by the containers in §1.

Staff sign-in is not wired into the API yet; until it is, every endpoint is anonymous locally as well.

## 6. Lifecycle — nothing persists

PostgreSQL and Azurite have no data volume, so each run gets an empty database and empty blob storage. Data created in the previous run is gone. ClamAV's signatures are the only exception (§4).

This is deliberate: every run starts from the same state, and the schema comes from `configs/postgres/init/01-create-db.sql` ([ADR-D002](../db/decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production)) rather than from whatever an earlier run left behind. The AppHost copies `configs/postgres/init/` into the container's `/docker-entrypoint-initdb.d`, and PostgreSQL runs the scripts in name order against `POSTGRES_DB` (`healthyhires`) when the empty data directory is initialised. Seed scripts go next to it as `02-…sql`.

### Adding a data volume

`.WithDataVolume()` on the `postgres` resource makes the database survive restarts. The costs:

- **Every run stops being identical.** A schema or seed change needs the volume removed by hand, and a teammate's broken data is not reproduced on another machine.
- **Init scripts run once.** PostgreSQL executes `configs/postgres/init/` only against an empty data directory, so a schema change no longer reaches your database until you remove the volume.

Do it locally if it helps a particular task, but do not commit it.

## 7. Troubleshooting

| Symptom | Cause |
| ------- | ----- |
| A resource fails with "address already in use" | Its pinned port (§1) is taken, often by a second AppHost or a `just edge` left running; stop the other process |
| `apiservice` stays in "Waiting" | `postgres`, `blobs` or `mailpit` is not healthy yet; their logs in the dashboard say why |
| ClamAV unhealthy for up to a minute after start | Normal — clamd is loading its signatures (§4) |
| ClamAV keeps failing to download signatures | The mirror rate-limited this machine; wait, and keep the `healthyhires-clamav-db` volume rather than resetting it |
| The dashboard asks for a token | It was opened without the login link; copy the URL with the token from the AppHost's console |
| The frontend gets proxy errors on `/api` | The API is not running, or not on `http://localhost:5180`; set `API_URL` in `App/.env` ([getting-started.md](../app/getting-started.md)) |
| Data from the previous run is missing | Expected — nothing persists (§6) |
