# Contract Testing

The API publishes an OpenAPI document generated from the code that serves the requests ([ADR-A004](ADR-ASPNET.md#adr-a004-api-style--rest-openapi-scalar-and-versioning)). The React frontend is built against it, and Scalar renders it as documentation. A generated document is trusted because nobody writes it by hand — which is also why nobody notices when it stops describing what the API actually does.

[Schemathesis](https://schemathesis.readthedocs.io/) closes that gap. It reads the document, generates requests from it, and checks every response against what the document promised. [ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis) chose it; this document describes how it is wired up for HealthyHires.

**Status:** planned. The files and commands below are the intended setup and do not exist yet.

## 1. Setup

| Piece | Where |
| ----- | ----- |
| Run script | `scripts/contract-test.sh` (TODO) |
| Run configuration | `configs/schemathesis/schemathesis.toml` (TODO) |
| Request hooks | `configs/schemathesis/hooks.py` (TODO) |
| Image | Official Schemathesis container image, pinned by digest |
| Test identity | A dedicated Entra ID test user (§2) |
| OpenAPI document | `/openapi/v1.json`, served by the API |
| Working files | `.schemathesis/` — git-ignored |

The run targets the **local stack**: the API with PostgreSQL, Azurite, ClamAV and Mailpit containers in place of the Azure services ([iam.md §8](iam.md#8-local-development)). It never targets production, because generated requests write real data.

```bash
./scripts/contract-test.sh                 # the whole document
./scripts/contract-test.sh applications    # only operations whose path matches the pattern
```

The script mints a token (§2), starts the pinned Schemathesis container on the stack's network, and points it at the document the API serves. Schemathesis is not a long-running service: it runs to completion and exits, like k6 ([ADR-018](ADR.md#adr-018-load-and-performance-testing--k6)) and ZAP ([ADR-020](ADR.md#adr-020-dynamic-application-security-testing--owasp-zap)).

`.schemathesis/` holds the Hypothesis example database, which lets a failing case be re-run instead of re-discovered, and any reports. Reports that record request headers contain a live access token, which is why the folder is git-ignored.

## 2. Getting a Token Without a Browser

Staff endpoints need an Entra ID access token. Entra's password grant (ROPC) fails as soon as MFA is required, and the free tenant's security defaults enforce MFA. The token is therefore borrowed from the **Azure CLI**, which handles MFA interactively once and then refreshes tokens on its own:

1. In the **HealthyHires API** app registration, under **Expose an API**, add the Azure CLI (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) as an authorized client application for the `access_as_user` scope.
2. Assign the test user an app role on the API's enterprise application — otherwise Entra refuses the token because *Assignment required* is on ([iam.md §2.1](iam.md#21-app-registrations)).
3. Sign in once as the test user with `az login`, then let the script mint tokens:

   ```bash
   az account get-access-token --scope api://<api-client-id>/.default --query accessToken -o tsv
   ```

Details that make the token *useful*:

- **The test user holds the `Owner` role**, so every staff endpoint is reachable. Per-role authorization is tested elsewhere (§5).
- **The token's client is the Azure CLI, not the SPA.** If the API ever restricts which client applications may call it, the Azure CLI must stay allowed in the local stack — and only there.
- **The token belongs to a real user**, so endpoints that depend on "who is signed in" (the `oid` claim) work. A client-credentials token would have no user behind it.
- **It lasts about an hour**, which is enough for one run; the script requests a fresh one each time.

Anonymous applicant endpoints need no token at all.

## 3. What It Tests

### 3.1 Phases

Schemathesis runs in phases, and each finds a different kind of defect:

- **Examples** replays the `example` values the document itself advertises. Failures here are the least arguable: the document said the request was valid.
- **Coverage** is systematic: it omits each required parameter in turn, sends every enum value and pushes each boundary. It finds operations that return a status code they never declared.
- **Fuzzing** generates valid data the API must accept and invalid data it must reject. It finds rules enforced in code but missing from the document.
- **Stateful** chains operations — for example, list job offers, then fetch one by the id the list returned. It follows OpenAPI `links`, which endpoints declare with `.ProducesLink(...)` ([ADR-A004](ADR-ASPNET.md#adr-a004-api-style--rest-openapi-scalar-and-versioning), [implementation-guide.md §5.1](../api/implementation-guide.md#51-linking-a-response-to-what-it-unlocks)). It is the only phase that tests a sequence instead of a single call. A link from a list fires only when the list is non-empty, so the local stack needs seed data: open job offers, applications and staff records.

### 3.2 Checks

All checks are enabled:

- `not_a_server_error` — a 5xx is a failure whatever the request was. Malformed input must be a 400.
- `status_code_conformance` — the status code is one the operation documents.
- `response_schema_conformance` — the body matches the documented schema for that status.
- `content_type_conformance`, `response_headers_conformance` — the media type and required headers match.
- `positive_data_acceptance`, `negative_data_rejection` — schema-valid data is accepted and schema-violating data is rejected. Together they show whether the document and the validation rules agree.
- `missing_required_header` — omitting a required header is rejected. Here that is any header declared with `.RequiresHeader(...)` ([implementation-guide.md §5.3](../api/implementation-guide.md#53-declaring-a-required-header)), and `Idempotency-Key` on commands ([ADR-A016](ADR-ASPNET.md#adr-a016-idempotency--idempotency-key-middleware)); read-only `POST` endpoints mapped with `MapQuery` do not require it ([ADR-A015](ADR-ASPNET.md#adr-a015-read-only-endpoints-with-a-request-body--post-now-query-later)).
- `unsupported_method`, `allow_header_conformance` — an undocumented method gets `405` with an accurate `Allow` header.
- `ignored_auth` — every case is re-sent without credentials and must return `401`. It is the only automated proof that every staff endpoint requires authentication. It applies only to operations that declare a security requirement, so **public applicant endpoints must not declare one** — and staff endpoints must.
- `use_after_free`, `ensure_resource_availability` — a resource is gone after a delete and present after a create.

## 4. HealthyHires-Specific Configuration

### 4.1 The applicant endpoints

- **ALTCHA:** Schemathesis cannot solve proof-of-work challenges, so every application request would be rejected before reaching the code under test. The local stack disables ALTCHA verification with a configuration flag that must never be set in production ([ADR-022](ADR.md#adr-022-bot-protection--altcha-on-the-application-form)).
- **Per-IP rate limit:** all generated requests come from one address, so the anonymous budget is raised in the local stack together with the staff budget (§6.1).
- **Uploads:** the upload URL endpoint returns a SAS URL for Azurite, but Schemathesis cannot upload a file to it. The confirm endpoint therefore mostly sees "blob not found". That response must be documented, or every case fails `status_code_conformance`. The real upload flow — browser → Blob Storage → ClamAV — is tested by Playwright ([ADR-019](ADR.md#adr-019-end-to-end-testing--playwright)).

### 4.2 Request hooks

`hooks.py` adjusts generated requests, but only where the generator cannot produce a meaningful value:

- **`Idempotency-Key`:** if a case already has the header, replace it with a fresh random UUID, so the middleware never replays a response. Never *add* the header when the case omitted it — that is the `missing_required_header` check's own deliberately broken case.
- **Path ids:** where a generated id is a well-formed UUID, it may be replaced with a seeded id so the happy path is reached. Malformed ids are left alone, because they are the `negative_data_rejection` check's cases.
- **Opaque cursors:** a keyset cursor is a token the API issued, so a generated one is always invalid. Drop it to request the first page; paging is still tested by the stateful phase, which follows the cursor of a real response.

## 5. What It Cannot Test

A green run is easy to over-read. It says nothing about:

- **Business rules** — for example, that an application cannot be submitted to a closed job offer. These belong in the xUnit suite.
- **Authorization per role** — the run uses one `Owner` identity, so it cannot show that an `HR.Worker` is refused an admin operation. Role policies are covered by xUnit tests.
- **Idempotency semantics** — deliberately neutralised by fresh keys (§4.2). The replay, `409` and `422` behaviour is pinned in the xUnit suite.
- **Bot protection and uploads** — ALTCHA is disabled and files never reach Azurite (§4.1).
- **Staff sign-in** — the token comes from the Azure CLI; the browser sign-in flow belongs to Playwright.
- **The edge** — the run targets the API directly, so headers set by Caddy are not in the picture. Pointing the run at Caddy's address runs the same cases through the proxy; ZAP covers the edge as well.

## 6. Pitfalls

### 6.1 The rate limit turns a red run green

The API gives each caller a request budget ([ADR-A007](ADR-ASPNET.md#adr-a007-rate-limiting--aspnet-core-rate-limiting)), and `429` is a documented response. A throttled case therefore **passes** every conformance check without the endpoint ever running. Queued requests are worse: they wait up to the whole window and come back as network errors that say nothing about the API.

The committed configuration keeps the client below the API's budget, which makes a run trustworthy but slow. For a fast working loop, raise both together in the local stack:

```bash
# API
RateLimiting__PermitLimit=6000
RateLimiting__QueueLimit=0
# schemathesis.toml: client rate limit "5000/m"
```

Raise both or neither: raising only the client turns the run into a test of `429` handling, and raising only the API changes nothing.

### 6.2 A helper that rewrites the case defeats the check

A hook or setting that always fixes up a request also fixes up the cases a check broke on purpose. That is why the hooks in §4.2 only replace values the generator produced legitimately.

The same trap applies to authentication. If Schemathesis generates the `Authorization` header from the document's security scheme *and* the script adds one, the `missing_required_header` and `ignored_auth` checks strip one header while the other remains. Disable generation of security parameters so the credential comes from one place only.

### 6.3 A failure early in the run hides everything after it

A run stops after its maximum number of failures. If one systematic defect fills that quota during the early phases, the stateful phase never runs — and the run reports only what it reached. Fix systematic failures first, then look at the rest.

## 7. Known .NET Pitfalls to Handle Up Front

These are classes of defects that Schemathesis reliably finds in ASP.NET APIs backed by PostgreSQL. Handling them before the first run saves a lot of noise:

- **NUL bytes in text:** PostgreSQL `text` cannot store `0x00`, so a string containing `\u0000` becomes an unhandled `500` on every endpoint that persists it. Map PostgreSQL error `22021` to `400` in one place.
- **Timestamps with an offset:** Npgsql writes `timestamptz` only from a `DateTimeOffset` with offset zero, so `2026-10-11T10:00:00+02:00` becomes a `500`. Normalise to UTC at the boundary, and check that the schema still describes the field as a date-time afterwards.
- **`[Required]` does not reject `Guid.Empty`:** a non-nullable `Guid` is never null, so an empty body binds to all zeros and passes validation. Add an explicit "not empty" rule next to `[Required]`.
- **Validation on route and query parameters is invisible in the document:** attributes on minimal-API parameters do not reach the schema, so the document permits values the API rejects. Describe those rules in the schema explicitly.
- **`[StringLength]` counts UTF-16 units:** JSON Schema `maxLength` and PostgreSQL `VARCHAR(n)` count code points, so an emoji near the limit is rejected by the API although both the schema and the database accept it.

## 8. Making It a Gate

[ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis) makes Schemathesis a CI gate once it passes consistently. A gate that is red from day one teaches people to ignore it, so it is switched on only when these are true:

- [ ] The document and the API agree on error shapes and status codes.
- [ ] A run is reproducible: a pinned `--seed`, or a lower example count with deterministic generation, so a failure means a regression instead of a newly generated case.
- [ ] Every CI run gets a fresh local stack with freshly seeded data, created and destroyed per run.
- [ ] The test user's token can be obtained in CI without an interactive `az login` (to be designed).

Fuzzing is stochastic: different seeds find different cases. Keep the seed printed in every summary, and treat a green run as "no known class of failure reproduced", never as "no defects".
