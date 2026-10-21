# Identity & Access Management

**Scope:** This document covers human and non-human identity in HealthyHires: who the actors are, how each one authenticates, what it is allowed to do, and where every credential lives. It uses three identity mechanisms:

- **Microsoft Entra ID** — the company's existing Microsoft 365 tenant, used for staff sign-in ([ADR-004](ADR.md#adr-004-identity-provider--microsoft-entra-id-oidc)). Applicants have no identity at all.
- **The VM's managed identity** — an Entra identity that Azure creates for the VM. Workloads use it to reach Azure services without stored passwords or keys.
- **GitHub Actions identities** — the CI pipeline's short-lived GitHub and OIDC tokens, plus two stored secrets for deploying to the VM.

**Out of scope:**

- Host hardening and runtime detection; see [ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign) and [ADR-011](ADR.md#adr-011-runtime-container-security--falco).
- GDPR classification of applicant data; out of scope for this school project ([ADD §3.2](ADD.md#32-out-of-scope)).

## 1. Actors

| Actor | Identity mechanism | Represents | Can access |
| ----- | ------------------ | ---------- | ---------- |
| **Applicant** | None (anonymous) | A member of the public applying for a job | Public job listings; submitting one application with a CV and cover letter |
| **Staff user** | Entra ID (OIDC) | A recruiter, hiring manager or administrator with a company work account | Staff features, limited by their app role |
| **Workload** | VM managed identity | The containers running on the VM (mainly the API and the OpenTelemetry Collector) | Key Vault, Blob Storage, PostgreSQL, ACS Email, Azure OpenAI and Azure Monitor, limited by Azure role assignments; Microsoft Graph, to create staff accounts |
| **CI pipeline** | GitHub Actions tokens + stored deploy secrets | The GitHub Actions workflow | Pushing images, signing them, deploying to the VM over NetBird |
| **Operator** | Personal Entra, GitHub and NetBird accounts + SSH key | The person administering the platform | Azure portal, Entra admin, GitHub, the VM over SSH |

> **Why so few systems:** Azure gives the VM a managed identity, so workloads get tokens from Azure instead of from stored secrets. Staff already have Entra ID work accounts, and applicants need no account. HealthyHires therefore adds no identity system of its own.

## 2. Staff users — Microsoft Entra ID

### 2.1 App registrations

The tenant contains two app registrations, both **single-tenant** ("accounts in this organizational directory only"):

| App registration | Type | Purpose |
| ---------------- | ---- | ------- |
| **HealthyHires SPA** | Public client, redirect URI type *Single-page application* | The React frontend. Signs users in and requests access tokens for the API. Has no client secret. |
| **HealthyHires API** | Exposes an API (`api://<api-client-id>`) with one delegated scope, e.g. `access_as_user` | The ASP.NET backend. Defines the app roles and is the `aud` of every access token. |

**Assignment required = Yes** is set on both enterprise applications, so only users explicitly assigned to the application can sign in. An unassigned user is stopped by Entra with error `AADSTS50105` before reaching HealthyHires.

### 2.2 Sign-in and token lifecycle

1. The React app uses **MSAL.js** with the authorization-code flow and **PKCE**. It requests `openid profile` plus the API scope `api://<api-client-id>/access_as_user`.
2. The user signs in on `login.microsoftonline.com` with their work account and completes MFA if the tenant requires it.
3. Entra returns an **ID token** (who the user is, for the UI) and an **access token** (a JWT for the API) to the browser.
4. The React app sends the access token as `Authorization: Bearer <token>` on every API call.
5. The API validates it locally with ASP.NET Core's JWT bearer handler (`ApiService/Infrastructure/Auth/StaffAuthentication.cs`): signature against Entra's published signing keys (JWKS), issuer = the company tenant, audience = the API, and the token lifetime. Both v1.0 and v2.0 token formats are accepted. It never calls Entra per request.

| Token | Lifetime (Entra defaults) | Handling |
| ----- | ------------------------- | -------- |
| Access token | 60–90 minutes | MSAL renews it silently before it expires. |
| Refresh token (SPA) | 24 hours, rotated on every use | After 24 hours the user is redirected to Entra again; with an active Entra session this happens without a password prompt. |

**Revocation gap:** because tokens are validated locally, disabling a user in Entra does not invalidate an access token that was already issued. A disabled user can keep using the API until that token expires — at most about 90 minutes. This is accepted for an internal tool.

### 2.3 Roles and claims

Roles are **app roles** defined on the HealthyHires API app registration and assigned to users on its enterprise application. They arrive in the access token's `roles` claim.

| App role (value) | Who | Permissions |
| ---------------- | --- | ----------- |
| `Recruiter` | Recruiters (*náboroví pracovníci*), who run the hiring process | TODO — define per module |
| `HiringManager` | Hiring managers (*prijímajúci manažéri*), for whose team a position is filled | TODO — define per module |
| `Admin` | Administrators (*administrátori*) | Creates staff accounts (Recruitment, `POST /api/v1/recruitment/staff-users`); TODO — the rest per module |

Roles are assigned to individual users. Group-based assignment requires Entra ID P1, which a free tenant lacks.

The API enforces them with the policies in `Kernel.Auth.StaffPolicies`. `Recruiter` and `HiringManager` are peers; `Admin` is admitted by every policy:

| Policy | Admits |
| ------ | ------ |
| `staff` (also the default) | `Recruiter`, `HiringManager`, `Admin` |
| `staff-recruiter` | `Recruiter`, `Admin` |
| `staff-hiring-manager` | `HiringManager`, `Admin` |
| `staff-admin` | `Admin` |

Every policy also requires a token with the `access_as_user` scope (so app-only tokens are refused) and an `oid`. No valid token → `401`; a valid token with too low a role, or none → `403`.

Claims the API relies on:

| Claim | Meaning | Used for |
| ----- | ------- | -------- |
| `oid` | The user's object ID, stable across all apps in the tenant | Identifying the user in the database (e.g. who reviewed an application) |
| `sub` | Subject, stable for this app only | Rate-limit partition key ([ADR-A007](ADR-ASPNET.md#adr-a007-rate-limiting--aspnet-core-rate-limiting)) |
| `roles` | Assigned app roles | Authorization policies on endpoints |
| `tid` | Tenant ID | Validated to be the company tenant |
| `name`, `preferred_username` | Display name and sign-in name | Display only; never for authorization |

### 2.4 Session management

| Event | Handling |
| ----- | -------- |
| **Login** | MSAL redirect → Entra sign-in page → MFA if required → redirect back with an authorization code → MSAL exchanges it for tokens (PKCE). |
| **Account creation** | No self-registration ([ADR-004](ADR.md#adr-004-identity-provider--microsoft-entra-id-oidc)). An administrator creates a recruiter in HealthyHires: the API creates the Entra account as `username@<Entra:UserDomain>` with the `Recruiter` role and a random temporary password, returned once to the administrator and changed at the first sign-in. If the role cannot be assigned, the account is deleted again. Other roles are assigned in the Entra admin center. |
| **Logout** | MSAL `logoutRedirect` clears the tokens in the browser and ends the user's Entra session in that browser. |
| **Offboarding** | Disabling the account in Entra blocks new sign-ins and token renewal immediately; existing access tokens expire within ~90 minutes (§2.2). |
| **MFA** | Enforced by the tenant: security defaults on a free tenant, Conditional Access policies with Entra ID P1. HealthyHires does not implement MFA itself. |

## 3. Applicants — anonymous access

Applicants never authenticate. Every action they take is limited by controls that do not depend on knowing who they are:

| Control | What it limits | Reference |
| ------- | -------------- | --------- |
| **ALTCHA** proof-of-work | Automated submissions to the application form | [ADR-022](ADR.md#adr-022-bot-protection--altcha-on-the-application-form) |
| **Per-IP rate limiting** | Request volume per client, with a stricter budget on application endpoints | [ADR-A007](ADR-ASPNET.md#adr-a007-rate-limiting--aspnet-core-rate-limiting) |
| **Write-only SAS URL** | Uploading exactly one new blob, for a few minutes, after the API has checked that the job offer is open | [ADR-A010](ADR-ASPNET.md#adr-a010-file-storage--azure-blob-storage-via-azure-sdk) |
| **Idempotency key** (random UUID) | Duplicate submissions on retry | [ADR-A016](ADR-ASPNET.md#adr-a016-idempotency--idempotency-key-middleware) |

A SAS URL is a **bearer capability**: anyone holding it can use it until it expires. It is therefore kept short-lived, scoped to one blob and one permission, and never logged.

## 4. Workloads — the VM's managed identity

### 4.1 How it works

The VM has a **system-assigned managed identity**. Azure creates and rotates its credential; it is never stored on disk or visible to anyone. A workload asks the **Instance Metadata Service** (IMDS, `http://169.254.169.254`) for a token for a specific Azure service, and IMDS returns a short-lived access token. The Azure SDK (`DefaultAzureCredential`) does this automatically and refreshes tokens before they expire.

### 4.2 Role assignments

Each permission is an Azure role assigned to the managed identity, scoped as narrowly as the service allows:

| Service | Role | Scope | Used by | Purpose |
| ------- | ---- | ----- | ------- | ------- |
| Key Vault (secrets) | Key Vault Secrets User | Each secret individually | API, Collector | Read secrets at startup ([ADR-012](ADR.md#adr-012-secrets-store--azure-key-vault)) |
| Key Vault (keys) | Key Vault Crypto User | Each KEK individually | API | Wrap and unwrap data keys ([ADR-013](ADR.md#adr-013-key-management-kek--azure-key-vault)) |
| Blob Storage | Storage Blob Data Contributor | The storage account | API | Read and write module containers; create user delegation SAS URLs ([ADR-A010](ADR-ASPNET.md#adr-a010-file-storage--azure-blob-storage-via-azure-sdk)) |
| PostgreSQL | Login role for the managed identity, member of the NOLOGIN role `healthyhires_app`; created by the server's Entra admin (`scripts/azure/db-grant-identity.sh`) | The module schemas listed in `infra/db-schemas.txt`, owned by `healthyhires_app`; no right to create other schemas | API | Log in without a password ([ADR-006](ADR.md#adr-006-primary-database--postgresql-managed)) |
| ACS Email | A role on the ACS resource that allows sending email (least-privilege role TODO) | The ACS resource | API | Send notification emails ([ADR-010](ADR.md#adr-010-email-delivery--azure-communication-services-email)) |
| Azure OpenAI | Cognitive Services OpenAI User | The Azure OpenAI resource | API | Call the deployed model to score CVs; cannot read keys or change deployments ([ADR-024](ADR.md#adr-024-cv-scoring--azure-openai-gpt-4o-sweden-central)) |
| Microsoft Graph | Application permissions `User.ReadWrite.All`, `Application.Read.All`, `AppRoleAssignment.ReadWrite.All`; granted by a Privileged Role Administrator (`scripts/azure/graph-grant-identity.sh`) | The tenant | API | Create staff accounts and assign them an app role on the HealthyHires API (§2.4); list the staff and their roles, e.g. to check a job post's recruiter (`Application.Read.All` covers reading the role assignments) |
| Application Insights | Monitoring Metrics Publisher | The Application Insights resource | Collector | Only if the Collector sends telemetry with Entra authentication (see §9) |

### 4.3 The shared-identity risk

There is one managed identity per VM, and **every container on the VM can reach IMDS**. A compromised container — even ClamAV or Caddy — can request tokens with all of the permissions in §4.2. Current mitigations:

- Falco alerts on IMDS access from unexpected containers ([ADR-011](ADR.md#adr-011-runtime-container-security--falco)).
- Role assignments are scoped per secret, key and schema, so the identity holds no broad "Owner" or "Contributor" rights.

The Graph permissions are the exception: they cannot be scoped below the tenant. `User.ReadWrite.All` lets the holder create and change non-admin users, and `AppRoleAssignment.ReadWrite.All` lets it grant any app role in the tenant, including further Graph permissions to itself. A token stolen through IMDS is therefore a tenant-level compromise, not only a HealthyHires one, which makes restricting IMDS access (§9) more pressing.

Blocking IMDS for every container except the API and the Collector would remove the risk but needs per-container network rules (§9).

## 5. Secrets inventory

| Item | Type | Stored in | Used by | Notes |
| ---- | ---- | --------- | ------- | ----- |
| ALTCHA signing key | Secret | Key Vault | API | Signs and verifies proof-of-work challenges ([ADR-022](ADR.md#adr-022-bot-protection--altcha-on-the-application-form)) |
| Azure Monitor connection string | Secret | Key Vault, written by Bicep | Collector | Read at every Collector start by `hh-podman-secret` and passed as a Podman secret; removable if the Collector gains Entra authentication (§9) |
| Azure OpenAI API key | — | Does not exist | — | Key access is disabled on the resource; only Entra tokens are accepted |
| PostgreSQL admin password | — | Does not exist | — | Password authentication is disabled; the operator and the API both sign in with Entra tokens |
| Break-glass password | Secret | Key Vault only; the VM stores its hash | Operator, through Azure Serial Console | Generated by `provision.sh breakglass`; the `breakglass` user is denied over SSH (§7) |
| Key Encryption Keys | Key | Key Vault | API, backup jobs | Never leave the vault; only wrap/unwrap operations are allowed |
| Caddy TLS certificates + ACME account key | Key material | Caddy's persistent volume on the VM | Caddy | Managed by Caddy ([ADR-003](ADR.md#adr-003-edge--reverse-proxy--caddy)) |
| NetBird setup key (CI, ephemeral) | Secret | GitHub Actions secrets | CI | Each use creates a peer that is removed when the job ends |
| SSH deploy key | Secret | GitHub Actions secrets | CI | Accepted only on the NetBird interface |
| Managed identity credential | — | Azure-internal | — | Never visible; tokens are requested from IMDS |

Not secrets, by design: Entra signing keys are public (JWKS), the SPA has no client secret, and the API validates tokens without one.

## 6. CI pipeline identity

The pipeline holds no Azure credentials: it builds, signs and deploys without touching Azure APIs.

| Need | Identity | Lifetime |
| ---- | -------- | -------- |
| Push images to GitHub Container Registry | `GITHUB_TOKEN` with `packages: write` | One job |
| Sign images with Cosign (keyless) | GitHub OIDC token (`id-token: write`) exchanged with Sigstore for a signing certificate | Minutes |
| Join the admin network | NetBird ephemeral setup key | Peer removed after the job |
| Run the deploy on the VM | SSH key for a dedicated deploy user | Long-lived; rotate if GitHub is compromised |

The deploy user should be restricted to the deploy command (a forced command in `authorized_keys`), so a leaked CI key can deploy but not open a shell.

## 7. Operator access paths

| Control plane | How the operator authenticates | Can do | Notes |
| ------------- | ------------------------------ | ------ | ----- |
| **Azure portal** | Personal Entra account + MFA | Everything in the subscription | The highest-impact account; compromise means full control. |
| **Entra admin center** | Same account, with an Entra admin role | Create users, assign app roles, manage app registrations | Controls who can sign in to HealthyHires. |
| **VM over SSH** | NetBird peer + NetBird account + SSH key accepted by `sshd` | Administer the host | All three are required; public TCP 22 is closed ([ADR-014](ADR.md#adr-014-admin-access--netbird-operator-and-ci-runner-ssh)). |
| **Azure Serial Console** | Azure portal access + a local VM user password | Break-glass shell when NetBird or networking is broken | Requires boot diagnostics enabled on the VM. |
| **GitHub** | Personal GitHub account + 2FA | Change code and CI, read CI secrets indirectly through workflows | A compromised GitHub account can deploy to the VM through CI. |

## 8. Local development

- No Azure identity is used locally: Azurite, a PostgreSQL container, ClamAV and Mailpit replace the Azure services, and secrets come from .NET user secrets or environment variables.
- CV scoring is the exception: there is no local stand-in for the model, so a developer calls the real Azure OpenAI resource with their own Azure CLI sign-in. Bicep grants the operator Cognitive Services OpenAI User on it.
- Staff sign-in still uses the real Entra tenant; `http://localhost:5173`, the Vite dev server, is registered as an SPA redirect URI.
- Creating a staff account calls Microsoft Graph as the developer's Azure CLI sign-in, so it works only for a developer whose own Entra roles allow it (User Administrator and Application Administrator, or Global Administrator), and needs `Entra:UserDomain` as a user secret of the AppHost.
- Test tools (Schemathesis, k6, ZAP) get an access token for a dedicated test user via the Azure CLI ([ADR-009](ADR.md#adr-009-api-testing--contract-validation--schemathesis)); Playwright signs in with a TOTP-enabled test user ([ADR-019](ADR.md#adr-019-end-to-end-testing--playwright)).

## 9. Open items

- [x] The Collector authenticates to Azure Monitor with the connection string from Key Vault ([container-deployment.md §5](container-deployment.md#5-open-items)).
- [ ] Define the permissions of `Recruiter`, `HiringManager` and `Admin` per module.
- [ ] Confirm the least-privilege role for sending email through ACS with a managed identity.
- [ ] Restrict IMDS access to the API and Collector containers (§4.3).
- [ ] Narrow the managed identity's tenant-wide Graph permissions (§4.3), e.g. by moving staff-account creation to the administrator's own delegated token.
- [ ] Restrict the CI deploy user to a forced deploy command (§6).
