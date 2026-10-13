# Decision Log (ADR)

This decision log captures architecture and infrastructure decisions for the platform stack. Each entry follows the template:

- Title
- Status
- Context
- Decision
- Consequences
- Dates & Owner

Separate ADRs are maintained for application-specific decisions (see [ADR for ASP.NET Monolith](ADR-ASPNET.md) and [ADR for Frontend Application](ADR-FRONTEND.md)) to keep platform-level decisions distinct from application design choices.

---

## ADR-001: Hosting Platform — Azure VM

Status: Accepted

Context:

- Project targets EU hosting and simple operational model. Team prefers a small, fixed set of VMs for the monolith to reduce operational overhead.

Decision:

- Use a single Azure VM in an EU region as the primary hosting platform for the monolith and supporting services. No Kubernetes or managed container platforms will be used initially.

Consequences:

- Simpler operations and lower platform complexity.
- Need to manage OS hardening, patching, and process supervision on the VM.
- Limits horizontal scaling options; will require vertical scaling or more VMs for growth.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-002: Container Runtime — Podman (rootless where possible)

Status: Accepted

Context:

- Containers are desired for process isolation but Kubernetes is excluded. Podman provides daemonless, rootless container execution and integrates well on a VM.

Decision:

- Run application and supporting components in rootless Podman containers on the Azure VM, managed by systemd via Quadlet.

Consequences:

- Rootless containers; Caddy needs `net.ipv4.ip_unprivileged_port_start=80` to bind ports 80/443.
- Autorestart via systemd.
- Pods for multi-container components (monolith + gateway).
- Inability to update container images without downtime.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-003: Edge / Reverse Proxy — Caddy

Status: Accepted

Context:

- TLS termination and straightforward reverse-proxy configuration are required. Caddy provides both, with automatic certificate management.

Decision:

- Use Caddy as the public edge reverse proxy and TLS terminator in front of the ASP.NET application, with automatic HTTPS.

Consequences:

- Simple routing; certificates are issued and renewed by Caddy, no separate pipeline.
- Ports 80 and 443 must be open to the internet for ACME challenges.
- Caddy's data directory must be on a persistent volume to keep certificates across restarts.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-004: Identity Provider — Microsoft Entra ID (OIDC)

Status: Accepted

Context:

- The system is for internal use by ~20 employees. The company already uses Microsoft 365, so every user has an Entra ID work account with MFA.

Decision:

- Use the company's Entra ID tenant as the identity provider via OIDC, with a single-tenant app registration and app roles for authorization.

Consequences:

- No self-registration: accounts are pre-created by IT and only assigned users can log in.
- No passwords or MFA to build; offboarding is handled by IT.
- No additional cost; included in Microsoft 365.
- App registration and role assignments require a tenant admin.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-005: Object Storage — Azure Blob Storage

Status: Accepted

Context:

- Application stores user-uploaded assets and backups. The project must keep data in the EU. Azure Blob Storage is Azure's object storage, available in EU regions.

Decision:

- Use Azure Blob Storage for application assets and offsite backups, with Azurite as the local emulator for development and tests.

Consequences:

- Not S3-compatible; the application uses the Azure Blob SDK (`Azure.Storage.Blobs`).
- The VM accesses storage via its managed identity, so no access keys to store or rotate.
- Encryption at rest is enabled by default; implement lifecycle rules for backups.
- Uploaded files are scanned for malware by a self-hosted ClamAV container on the VM, so no paid scanning service is needed; ClamAV needs about 1–2 GB of RAM for its signature database, which the VM size must allow.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-006: Primary Database — PostgreSQL (managed)

Status: Accepted

Context:

- PostgreSQL is the primary persistent store. Trade-offs between managed DB (Azure Database for PostgreSQL) and self-hosted PostgreSQL on the VM include operational overhead, backup management, and control. Managed DB reduces operational burden and provides automated backups.

Decision:

- Use Azure Database for PostgreSQL – Flexible Server for production to reduce operational overhead and to get automated backups.

Consequences:

- Rely on managed service SLAs; minimizes DBA tasks but backup/restore and access controls must be validated.
- Secure connectivity between app and DB via private VNet access; the app authenticates with the VM's managed identity instead of a password.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-007: Observability — Azure Monitor + OpenTelemetry

Status: Accepted

Context:

- Need dashboards for host and container metrics, plus application-level traces, metrics and logs. Azure Monitor (Application Insights + Log Analytics) is Azure's managed observability backend, and OpenTelemetry is a vendor-neutral standard for instrumentation supported in ASP.NET.

Decision:

- Instrument the application with OpenTelemetry for traces, metrics and logs.
- Use Azure Monitor as the backend for storage, dashboards and alerting.

Consequences:

- No separate monitoring stack to host or operate.
- Instrumentation stays vendor-neutral; changing the backend only changes exporter configuration.
- Azure Monitor bills per GB ingested (first 5 GB/month free), so ingestion volume is the cost lever, not retention.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-007.1: Telemetry Pipeline — OpenTelemetry Collector (otelcol-contrib)

Status: Accepted

Context:

- ADR-007 picks Azure Monitor + OpenTelemetry but leaves the pipeline unspecified. Exporting directly from the app gives no shared place for redaction, sampling, routing or buffering during outages, and doesn't cover host and container telemetry.
- The core Collector distribution lacks the Azure Monitor exporter and the receivers needed for host and container telemetry (`hostmetrics`, `journald`, `filelog`, `podman_stats`).

Decision:

- Run the OpenTelemetry Collector as a Podman sidecar on the VM. The app sends OTLP to the Collector on localhost; the Collector also collects host and container telemetry and exports everything to Azure Monitor.
- Use the `otelcol-contrib` distribution, pinned by digest and verified with Cosign ([ADR-008](#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign)), with only the components listed in the Collector configuration enabled.

Consequences:

- One agent on the host and one place to add sampling, redaction or new sinks without touching code.
- If the Collector goes down, the app keeps working; only telemetry is lost until systemd restarts it.
- Larger image with many unused components; Trivy will report CVEs in components we don't enable, which must be triaged as "not enabled".
- The Collector reads host files (the journal export, the audit log, `/proc`), so its container runs without SELinux separation; Falco is the only other container with such an exception.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-008: Security & Supply-chain Tools — Trivy, OpenSCAP, AIDE, Cosign

Status: Accepted

Context:

- CI/CD must provide container/image vulnerability scanning, host hardening checks, and image signing to meet security/compliance objectives.

Decision:

- Adopt Trivy for container and dependency scanning, OpenSCAP for host configuration-compliance checks, AIDE for integrity monitoring, and Cosign for signing and verifying OCI images.
- Use the AlmaLinux 10 CIS Level 1 Server profile as the VM baseline; record every excluded rule as an approved deviation. Production scans are audit-only.

Consequences:

- Integrate Trivy and Cosign into CI pipelines and require signed images for production deployments.
- Schedule periodic OpenSCAP checks on the VM and track failed rules as remediation work.
- Use AIDE to monitor filesystem integrity and alert on unexpected changes.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-009: API Testing / Contract Validation — Schemathesis

Status: Accepted

Context:

- We need automated contract and property-based testing for the API to catch regressions and ensure the generated OpenAPI document matches what the API actually does.

Decision:

- Use Schemathesis to test the API against its own OpenAPI document, run on demand against the local stack. Make it a CI gate once it passes consistently.

Consequences:

- The OpenAPI document must be kept accurate; once gated, CI fails on contract breaks.
- Runs need an Entra ID access token; a dedicated test user in the dev tenant provides it.
- Runs write real data, so they target a disposable database only.
- Setup, token acquisition and gate criteria are described in [contract-testing.md](contract-testing.md).

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-010: Email Delivery — Azure Communication Services Email

Status: Accepted

Context:

- The application requires reliable email delivery for notifications and transactional emails.
- The team prefers to avoid managing a self-hosted SMTP server due to operational overhead and deliverability concerns. Azure Communication Services (ACS) Email is Azure's managed email service with a Europe data location.

Decision:

- Use ACS Email with an Azure-managed sender domain (`DoNotReply@<id>.azurecomm.net`), sent via the ACS Email SDK and authenticated with the VM's managed identity. The project has no domain of its own; a verified custom domain and delivery-status events via Event Grid are added if it gets one.

Consequences:

- Dependence on an external service for email delivery, but no additional cloud provider.
- No DNS records to manage, but the sender is an Azure subdomain, and bounces are not tracked until Event Grid is added.
- Default sending limits for new ACS resources are low; sufficient for internal use, but raising them requires a support request.
- Attachments are sent inline with the request (10 MB limit), so no separate attachment storage is needed.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-011: Runtime Container Security — Falco

Status: Accepted

Context:

- [ADR-008](#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign) covers build-time and host-baseline security but not runtime behaviour: a signed container can still exec a shell, open unexpected connections or read sensitive files after an exploit. Falco (CNCF, eBPF-based) detects this on a single Podman host without Kubernetes.

Decision:

- Run Falco as a privileged Podman container on the VM, with an initial rule set for unexpected process execution in the API container, egress outside known destinations (PostgreSQL, Collector, ClamAV, Azure endpoints) and reads of sensitive files.
- The ClamAV container's only allowed egress is HTTPS (TCP 443) from its `freshclam` signature updater; any other outbound connection from that container alerts.
- Falco alerts are collected by the OpenTelemetry Collector and exported to Azure Monitor ([ADR-007.1](#adr-0071-telemetry-pipeline--opentelemetry-collector-otelcol-contrib)).

Consequences:

- One more container image and rules file to maintain; Falco is the one privileged container on the host.
- Falco only detects, it does not block, and is not in the request path, so a bad rule causes noise or missed detections, not outages.
- Alerts require somebody to act on them.
- ClamAV's signature mirror sits behind a CDN with changing IP addresses, so its rule is scoped by process and port rather than by destination address.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-012: Secrets Store — Azure Key Vault

Status: Accepted

Context:

- Most Azure services used (Blob Storage, PostgreSQL, ACS Email, Azure OpenAI, Entra ID) are reached via the VM's managed identity, so few secrets remain. The rest need a managed, access-controlled store instead of files or environment variables in the repo.

Decision:

- Store remaining secrets in Azure Key Vault. Workloads read them at startup via the VM's managed identity, with read access granted per secret (Key Vault Secrets User role).

Consequences:

- No secret is stored on disk or in CI; access is audited by Key Vault.
- The managed identity is shared by every process and container on the VM, so a compromised container can request its tokens; Falco alerts on unexpected access to the instance metadata endpoint.
- Key Vault becomes a startup dependency; if unreachable, workloads fail to start.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-013: Key Management (KEK) — Azure Key Vault

Status: Accepted

Context:

- Azure encrypts Blob Storage and PostgreSQL at rest by default. Data that needs encryption beyond that (e.g. database backups exported to Blob Storage) uses envelope encryption: a Key Encryption Key (KEK) in a managed KMS wrapping per-blob Data Encryption Keys (DEKs).

Decision:

- Use Azure Key Vault keys as the KEK provider for all envelope-encrypted data.

Consequences:

- No own HSM or key infrastructure to operate; HSM-backed keys require the Premium tier.
- Cost is per key per month plus per-operation charges; tiny for our volume.
- Rotation: a new key version does not re-encrypt existing data.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-014: Admin Access — NetBird (operator and CI-runner SSH)

Status: Accepted

Context:

- An Azure VM with a public IP exposes SSH on TCP 22 to brute-force and SSH CVEs. Only operators and the CI runner need SSH access.

Decision:

- Use NetBird (free tier) as a WireGuard-based admin mesh. SSH is accepted only on the NetBird interface (firewalld allows it in that interface's zone alone), and the NSG blocks TCP 22 on the public IP; NetBird needs no inbound port.
- The CI runner joins with an ephemeral setup key, so its peer is removed when the job ends. The groups and access policies are written down in the deployment guide and applied by hand in the NetBird dashboard.

Consequences:

- No public SSH; an operator needs NetBird membership and an authorised SSH key.
- If the NetBird agent or VM networking fails, there is no network path in; Azure Serial Console is the break-glass path.
- External dependency on NetBird's managed control plane.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-015: Operating System — AlmaLinux 10

Status: Accepted

Context:

- The VM's OS must support Podman, SELinux and modern eBPF (for Falco), match the CIS baseline in [ADR-008](#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign), and avoid a paid Red Hat subscription.

Decision:

- Use the AlmaLinux 10 image from the Azure Marketplace, configured at first boot with cloud-init.

Consequences:

- RHEL-compatible OS without subscription cost.
- SELinux is on by default; misconfigured SELinux labels are a common cause of containers failing to start.
- Patching is `dnf`-based; EPEL and CRB repositories are enabled only as needed.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-016: External Uptime Probe — Application Insights Availability Tests

Status: Accepted

Context:

- Health checks run by the Collector on the VM can't see failures in DNS, TLS or Caddy, and go silent when the VM itself is down. Availability has to be measured from outside the VM.

Decision:

- Use an Application Insights standard availability test against the public health endpoint from one EU location every 15 minutes, with an Azure Monitor alert on failure.

Consequences:

- Measures the full public path (DNS, TLS, Caddy, app) independently of the VM.
- Same vendor as the hosting; an Azure-wide outage can hide both the failure and the alert.
- Sign-in is not probed, because Entra ID MFA blocks scripted logins.
- One location keeps the per-run cost low, but a network problem at the probe's location looks like an outage; the test retries once before it counts a failure.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-017: auditd Rule Scope — CIS Baseline with Documented Deviations

Status: Accepted

Context:

- `auditd` on AlmaLinux ([ADR-015](#adr-015-operating-system--almalinux-10)) is the forensic record of host activity. Adopting a published baseline is easier to defend than a custom rule set.

Decision:

- Adopt the CIS auditd rule set for RHEL-family hosts, with documented deviations: the unsuccessful-file-access rules are disabled (high volume, no forensic value on a container host), and a full audit partition must not halt the host.
- Audit logs are collected by the OpenTelemetry Collector and exported to Azure Monitor.

Consequences:

- Kernel overhead and log volume must be measured; audit logs count toward the Azure Monitor ingestion cost.
- Audit records can contain personal data (command lines), so they are treated as sensitive and given a defined retention.
- `auditd` only records; detection stays with Falco ([ADR-011](#adr-011-runtime-container-security--falco)).

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-018: Load and Performance Testing — k6

Status: Accepted

Context:

- Capacity and latency targets have never been measured. Production is a single small VM, so its saturation point is worth knowing before users find it.

Decision:

- Use k6 for load and performance tests, run on demand against the local stack and never against production. Scripts live in `tests/k6/`.

Consequences:

- Local results are comparative, not production numbers.
- No CI gate until a stable baseline exists.
- Runs need an Entra ID access token, obtained the same way as for [ADR-009](#adr-009-api-testing--contract-validation--schemathesis).

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-019: End-to-End Testing — Playwright

Status: Accepted

Context:

- Critical flows such as sign-in and the main user workflows only exist end to end; unit and contract tests can't observe them.

Decision:

- Use Playwright for browser end-to-end tests against the web frontend on the local stack. Sign in once in a setup step with a dedicated Entra ID test user and reuse the saved session in all tests.

Consequences:

- Slowest test suite; runs before merge, not on every commit.
- The test user's MFA must work unattended: register a TOTP authenticator for it and generate codes in the setup step.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-020: Dynamic Application Security Testing — OWASP ZAP

Status: Accepted

Context:

- [ADR-008](#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign) covers images and the host, and [ADR-009](#adr-009-api-testing--contract-validation--schemathesis) covers the API contract. Nothing tests the running application from the outside for header, authentication and injection issues.

Decision:

- Use OWASP ZAP against the local stack, scanning through Caddy: a passive baseline scan as a CI gate, and an authenticated full scan on demand.

Consequences:

- API rate limiting must be lifted during scans; a throttled scan reports clean without testing anything.
- False positives are suppressed in the scan configuration with a reason, not ignored.
- The active scan writes to the database, so it only targets a disposable one.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-021: Fault Injection — Toxiproxy

Status: Accepted

Context:

- Tests only ever see healthy dependencies. The failures that hurt in production (hung connections, resets mid-transaction, slow responses) need a proxy between the app and its dependencies.

Decision:

- Use Toxiproxy via Testcontainers in integration tests, and as an optional local-stack service proxying PostgreSQL, Azurite and the API.

Consequences:

- Covers transport-level faults only (timeouts, resets, latency), not wrong data.
- Off by default in the local stack, because it rewires the app's dependency addresses.
- External Azure services such as Entra ID can't be put behind it.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-022: Bot Protection — ALTCHA on the Application Form

Status: Accepted

Context:

- Applicants apply anonymously through a public form that uploads files ([ADD §2](ADD.md#2-purpose)). Without protection, bots can flood it with fake applications, filling storage and HR's review queue.

Decision:

- Protect the application form with ALTCHA, a self-hosted proof-of-work challenge. The API issues challenges and verifies solutions in-process before issuing an upload URL or accepting an application, alongside per-IP rate limiting ([ADR-A007](ADR-ASPNET.md#adr-a007-rate-limiting--aspnet-core-rate-limiting)).

Consequences:

- No third-party service, no data transfer outside the EU and no extra container.
- Proof-of-work slows bots down but does not identify them; rate limiting remains necessary.
- The challenge-signing key is a secret stored in Key Vault ([ADR-012](#adr-012-secrets-store--azure-key-vault)); each solution is accepted only once until it expires.
- Alternative considered: Cloudflare Turnstile, free with stronger bot detection, but rejected because it adds a US processor for applicant data.

Dates & Owner:

- Decided: 10. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-023: Infrastructure as Code — Bicep

Status: Accepted

Context:

- The Azure resources (VM, network, PostgreSQL, Storage, Key Vault, ACS, Azure OpenAI, monitoring) and the managed identity's role assignments must be re-creatable. Clicking them together in the portal leaves no record, so a rebuild becomes guesswork.

Decision:

- Declare every Azure resource and role assignment in Bicep under `infra/`, applied by the operator with `az deployment sub what-if`, then `az deployment sub create` (`scripts/azure/provision.sh`). Entra ID objects and secret values stay outside the templates.

Consequences:

- No state file to protect: Azure itself is the state, and `what-if` shows drift.
- CI keeps no Azure credentials ([iam.md §6](iam.md#6-ci-pipeline-identity)); infrastructure changes are applied by hand.
- Deployments are incremental: a resource removed from a template is not deleted in Azure.
- Azure-only; OpenTofu was considered, but multi-cloud support is not needed and its state file would hold secrets.

Dates & Owner:

- Decided: 12. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-024: CV Scoring — Azure OpenAI (gpt-4o, Sweden Central)

Status: Accepted

Context:

- HR wants applications ranked by how well the CV fits the role's requirements, as an aid only, never an automatic rejection. The B2s VM cannot run a language model, and applicant data must stay in the EU.

Decision:

- Score CVs with Azure OpenAI `gpt-4o` (`2024-11-20`), a Standard deployment in Sweden Central, called by the API with the VM's managed identity once ClamAV has cleared the file. The model returns a verdict with quoted evidence per requirement; staff make every decision.

Consequences:

- Data is processed only in Sweden Central and billed to the Azure credit, about €0.02–0.03 per CV; no API key exists.
- Sweden Central is the only allowed region with Standard quota; `gpt-4o-mini` no longer accepts new deployments, and EU Data Zone quota for newer models is 0.
- The model retires on 14. 4. 2027; model and deployment name are configuration, so a successor replaces it without code changes.
- Ranking applicants with AI is high-risk under the EU AI Act; scores stay advisory and explained.

Dates & Owner:

- Decided: 13. 10. 2026
- Owner: Marek Dvorsky

---
