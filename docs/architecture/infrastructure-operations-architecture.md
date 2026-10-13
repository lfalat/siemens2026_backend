# Infrastructure / Operations Architecture

**Scope:** This document describes how the HealthyHires infrastructure is created, kept patched and changed: the region and environments, provisioning with Bicep, the host lifecycle, maintenance, drift and who operates it.

**Conventions:** One Azure VM in the EU region **Germany West Central** (Frankfurt, `germanywestcentral`) ([ADR-001](ADR.md#adr-001-hosting-platform--azure-vm)) running AlmaLinux 10 ([ADR-015](ADR.md#adr-015-operating-system--almalinux-10)), next to managed PostgreSQL, Blob Storage, Key Vault, ACS Email and Azure Monitor. Azure OpenAI runs in **Sweden Central**, the only allowed region with quota for it ([ADR-024](ADR.md#adr-024-cv-scoring--azure-openai-gpt-4o-sweden-central)). Every Azure resource is declared in Bicep ([ADR-023](ADR.md#adr-023-infrastructure-as-code--bicep)).

**Status:** the Bicep templates, cloud-init file, host scripts and operator scripts exist (`infra/`, `deploy/`, `scripts/azure/`) but have not yet been applied to a real subscription. The step-by-step procedure is the [Azure deployment guide](../operations/azure-deployment-guide.md). Open questions are collected in §8.

## 1. Document Boundaries

This document owns **how the infrastructure is created and maintained**. Other documents own the rest and are linked, not repeated:

| Topic | Owner |
| ----- | ----- |
| What runs on the VM, ports, network flows, Quadlets, application deploy and rollback | [container-deployment.md](container-deployment.md) |
| Identities, role assignments, secrets inventory, operator access paths | [iam.md](iam.md) |
| Image build, scanning and signing | [ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign), [C1 Supply Chain](../diagrams/architecture/C1_Supply_Chain.svg) |
| Telemetry pipeline, alerts and SLOs | [observability-architecture.md](observability-architecture.md) |
| Database backups, RPO/RTO and restore drills | `backup-recovery-plan.md` (TODO) |
| Local development | [aspire-dev-stack.md](aspire-dev-stack.md) |

## 2. Region and Environments

| Environment | Where | Created from | Data |
| ----------- | ----- | ------------ | ---- |
| **Local** | Developer machine | Aspire AppHost ([aspire-dev-stack.md](aspire-dev-stack.md)) | Throwaway, recreated on every run |
| **Production** | Germany West Central, resource group `rg-healthyhires-prod` | `infra/main.bicep` with `HH_ENVIRONMENT=prod` | Real data |
| **Rehearsal** (temporary) | Same region, resource group `rg-healthyhires-<name>` | The same templates with `HH_ENVIRONMENT=<name>`, locks and purge protection off | Synthetic only |

- There is **no standing staging environment**. A budget-limited student subscription cannot pay for a second VM and database that sit idle.
- When a change needs testing on real Azure resources (a new resource type, a cloud-init change, a rebuild drill), the operator deploys the same templates into a separate resource group, tests, and **deletes the whole resource group**. Deleting the group removes everything in it, so nothing is left running and billing.
- A rehearsal group gets its own managed identity and Key Vault, and no role assignment on production resources.
- Every resource is restricted to EU regions by an Azure Policy "allowed locations" assignment ([ADD §6.2](ADD.md#62-quality-attributes-with-measurable-targets)), also declared in Bicep. It is assigned to the resource group, not the subscription, because a student subscription usually holds other work too. It allows `germanywestcentral`, `swedencentral`, `polandcentral` and `austriaeast`: the EU regions that the Azure for Students subscription's own allowed-regions policy permits (it also permits `switzerlandnorth`, which is not in the EU). A teammate's subscription with a different student policy may need the list in `infra/modules/policy.bicep` adjusted.
- **The deployment is not tied to one subscription.** Everything specific to a person or subscription is in the gitignored `infra/azure.env`; the same templates and scripts provision a teammate's subscription when a student credit runs out ([guide §11](../operations/azure-deployment-guide.md#11-moving-to-a-teammates-subscription)).

## 3. Provisioning with Bicep

### 3.1 Who owns what

| Layer | Managed by | Examples |
| ----- | ---------- | -------- |
| Azure resources and role assignments | **Bicep** (`infra/`) | VNet, subnets, NSG, public IP and DNS label, VM with system-assigned identity and boot diagnostics, PostgreSQL Flexible Server, storage account and module containers, Key Vault, ACS Email, Azure OpenAI account and model deployment, Log Analytics, Application Insights, availability tests, alert rules and action group, role assignments from [iam.md §4.2](iam.md#42-role-assignments), the allowed-locations policy |
| First-boot host setup | **cloud-init** (`infra/cloud-init.yaml`, with the scripts from `deploy/host/` embedded by Bicep as custom data) | Users, linger, sysctl, packages, firewall, timers, `hh-deploy` ([container-deployment.md §3.1](container-deployment.md#31-the-vm)) |
| Host configuration and workloads | **Release bundle** built by CI (`deploy/render-bundle.sh`) and installed by `hh-deploy` ([container-deployment.md §3.3](container-deployment.md#33-deployment-and-rollback)) | Quadlets, environment files, Collector configuration, Falco rules, image digests |
| Values the deploy needs | **GitHub environment** `production`, written by `scripts/azure/provision.sh github` | Hostname, endpoints, PostgreSQL login role, the VM's NetBird address and SSH host key |
| Entra ID objects | **Manual**, Entra admin center | App registrations, app roles, users and role assignments ([iam.md §2](iam.md#2-staff-users--microsoft-entra-id)) |
| Secret values | **Manual**, `az keyvault secret set` | The ALTCHA signing key ([iam.md §5](iam.md#5-secrets-inventory)) |
| Admin mesh | **Manual**, NetBird dashboard | Peers, groups and access policies ([ADR-014](ADR.md#adr-014-admin-access--netbird-operator-and-ci-runner-ssh)) |

Each setting has exactly one owner. A setting changed in the portal that belongs to Bicep is drift (§6.2), not a second way to configure it.

Entra objects stay manual because there are few of them, they rarely change, and accounts are pre-created by an admin anyway. Bicep's Microsoft Graph extension could declare them later without changing anything else here.

### 3.2 Layout

```text
infra/
├── main.bicep          # subscription scope: creates the resource group and wires the modules together
├── main.bicepparam     # reads every per-person value from the environment (infra/azure.env)
├── budget.bicep        # subscription budget, a separate deployment so an offer without budgets does not block
├── db-schemas.txt      # one PostgreSQL schema per module, created by db-grant-identity.sh
├── azure.env.example   # template for the gitignored infra/azure.env
├── cloud-init.yaml     # first boot; embeds deploy/host/*
└── modules/            # policy, network, vm, postgres, storage, keyvault, email, openai, monitoring
scripts/azure/          # provision.sh, enrol-netbird.sh, db-grant-identity.sh, migrate.sh
```

- **Names** follow the Azure abbreviations with the environment as a suffix, e.g. `vm-healthyhires-prod`, `rg-healthyhires-prod`. Globally unique names (storage account, Key Vault, PostgreSQL) get a 6-character `uniqueString()` suffix derived from the subscription, so they differ on every subscription without anyone choosing them.
- **API versions** are fixed per resource in the templates and updated deliberately, never floating.
- **Parameter files hold no secrets.** A template never takes a secret value as a parameter: PostgreSQL uses Entra authentication with the operator as its Entra admin, and Key Vault secret values are set separately (§4.1).

### 3.3 Applying a change

Infrastructure changes are applied by the operator from their workstation, signed in with their personal Entra account ([iam.md §7](iam.md#7-operator-access-paths)). CI holds no Azure credentials ([iam.md §6](iam.md#6-ci-pipeline-identity)).

```bash
scripts/azure/provision.sh what-if
```

```bash
scripts/azure/provision.sh deploy
```

Both wrap `az deployment sub what-if|create` with `infra/main.bicep` and `infra/main.bicepparam`, after loading `infra/azure.env` and checking that the right subscription is selected.

1. Change the templates in a pull request; CI runs `az bicep build` and the linter, which need no Azure access.
2. After merge, run `what-if` and read every change. A **delete or replace** of the VM, PostgreSQL server, storage account or Key Vault stops the change until it is understood.
3. Run `create` with the same files. Apply infrastructure changes on their own, never while an application deploy is running.
4. Verify the affected resources (§4.2) and record the deployment name in the pull request.

Deployments are **incremental**: removing a resource from a template does not delete it from Azure. A resource that is no longer wanted is deleted explicitly with `az resource delete` after the template change, so the two stay in step.

The PostgreSQL server, storage account and Key Vault carry a `CanNotDelete` resource lock, declared in Bicep and switched off for rehearsal groups with `HH_ENABLE_LOCKS=false`, so a mistaken command cannot remove production data while rehearsal groups stay deletable. Key Vault also has soft delete and purge protection on.

## 4. Host Lifecycle

### 4.1 First provisioning

The commands are in the [Azure deployment guide](../operations/azure-deployment-guide.md) §6–§9.

1. **Resources:** `provision.sh check`, then `deploy` (§3.3). This also assigns the location policy. The VM boots and cloud-init runs.
2. **NetBird:** generate a single-use setup key with a short expiry in the NetBird dashboard and enrol the VM through Azure Run Command (`scripts/azure/enrol-netbird.sh`). The key is never placed in Bicep or cloud-init, where it would stay readable in the VM's provisioning data.
3. **GitHub:** `provision.sh github` copies the deployment's outputs, the VM's NetBird address and SSH host key, and the deploy key into the GitHub environment.
4. **Secrets:** Bicep stores the Application Insights connection string itself; any other Key Vault secret value is set with `az keyvault secret set`.
5. **Break-glass:** `provision.sh breakglass` sets a random Serial Console password for the `breakglass` user; the plaintext goes only to Key Vault.
6. **Database role:** `scripts/azure/db-grant-identity.sh`, as the server's Entra admin: a NOLOGIN role `healthyhires_app` owns the schemas, and the managed identity's login role is a member of it ([iam.md §4.2](iam.md#42-role-assignments)).
7. **First deploy:** run the CI deploy workflow ([container-deployment.md §3.3](container-deployment.md#33-deployment-and-rollback)).
8. **Readiness:** complete the checks in §4.2 before pointing the hostname at the VM or announcing it to staff.

### 4.2 Readiness checks

A host is ready only when all of these pass. A running container or a working SSH session is not proof on its own.

- [ ] SELinux is enforcing, and the OpenSCAP CIS Level 1 report has no failures other than recorded deviations ([ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign)).
- [ ] The AIDE baseline is initialised **after** the first deploy, so it records the intended state.
- [ ] TCP 22 is closed from the internet (tested from outside), and SSH works over NetBird ([ADR-014](ADR.md#adr-014-admin-access--netbird-operator-and-ci-runner-ssh)).
- [ ] Azure Serial Console opens a shell with the local break-glass account.
- [ ] Only TCP 80 and 443 answer on the public IP ([container-deployment.md §2.1](container-deployment.md#21-inbound-listeners)).
- [ ] Caddy has a valid certificate, and `/health` passes through the public hostname.
- [ ] Telemetry from the API, Caddy, the host, auditd and Falco arrives in Azure Monitor, and the availability test is green.
- [ ] A reboot brings every service back without intervention (cold start, [container-deployment.md §5](container-deployment.md#5-open-items)).

### 4.3 Rebuild and replacement

The VM holds no business data, so it is replaced rather than repaired when it is compromised or broken beyond a quick fix ([container-deployment.md §4.4](container-deployment.md#44-rotation-and-rebuild)):

1. Delete the VM and its OS disk, and remove its peer in NetBird.
2. Redeploy `main.bicep`. It recreates the VM, and with it a **new** system-assigned managed identity; the role assignments in the template follow it automatically.
3. Repeat §4.1 steps 2, 3, 5, 6, 7 and 8. The PostgreSQL login role is bound to the old identity, so `db-grant-identity.sh` creates one for the new identity and drops the old one; objects stay owned by `healthyhires_app`.

A compromised host is never cloned or snapshotted into its replacement.

## 5. Patching and Maintenance

### 5.1 Maintenance window

- Weekly window: **Sunday evening, Europe/Bratislava**. Staff use the application during working hours, so a few minutes of downtime on Sunday evening affects nobody in practice.
- Planned downtime is announced to staff in advance. Zero-downtime updates are not a goal ([ADR-002](ADR.md#adr-002-container-runtime--podman-rootless-where-possible)).
- **Critical** security updates are applied as soon as possible, not held for the next window.

### 5.2 What gets updated, and how

| Component | Update path | When |
| --------- | ----------- | ---- |
| OS packages | `dnf-automatic` downloads and reports available updates; the operator runs `dnf upgrade` and reboots if `dnf needs-restarting -r` says so | Weekly window |
| Container images | Rebuilt and rescanned in CI ([ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign)) with no Critical or High finding allowed except those accepted in `.trivyignore`, deployed by digest ([container-deployment.md §3.3](container-deployment.md#33-deployment-and-rollback)) | With releases, or when Trivy reports a fixable vulnerability |
| Upstream images (Caddy, ClamAV, Collector, Falco) | Digest bumped in a pull request, then deployed | Weekly window |
| ClamAV signatures | `freshclam`, automatically | Continuously |
| PostgreSQL minor versions | Azure, in a custom maintenance window set in Bicep to the same Sunday evening | Chosen by Azure within the window |
| Bicep API versions | Pull request, `what-if`, apply | When a feature or deprecation needs it |

Unattended production reboots are off: an update that needs a reboot waits for the operator, who verifies the host afterwards (§4.2, the reboot and health checks). After a patch, review the AIDE report and update its baseline only for changes the patch explains.

## 6. Changes, Rollback and Drift

### 6.1 Changes and rollback

- **Application** releases and rollbacks follow [container-deployment.md §3.3](container-deployment.md#33-deployment-and-rollback).
- **Infrastructure** changes follow §3.3. Rollback is reverting the commit and applying again; resources a change created must also be deleted by hand, because deployments are incremental.
- Application, infrastructure and host changes are made **one at a time**, so a failure points at one change.
- A change to the database schema rolls forward; an older image is never run against a newer schema.

### 6.2 Drift

- Run `what-if` against production in each maintenance window. Any difference means someone changed Azure outside Bicep.
- Explain every difference before acting on it. Then either bring the change into the template or apply the template to revert it; never let the two disagree.
- Changes made in the portal during an incident are allowed, but are brought into Bicep within a week.

## 7. Ownership and Monitoring

- **Operator:** Marek Dvorsky. There is no on-call rota and no substitute operator ([ADD §7.6](ADD.md#76-operations)); response is best effort.
- **Alerts** go by e-mail to the operator through an Azure Monitor action group declared in Bicep. E-mail does not wake anyone, so an alert outside working hours waits until it is read.
- **Watched by the operator:** the availability test ([ADR-016](ADR.md#adr-016-external-uptime-probe--application-insights-availability-tests)), missing-telemetry alerts, Falco alerts, OpenSCAP and AIDE reports, disk usage, and the subscription's remaining credit. The full alert list is in [observability-architecture.md §10.3](observability-architecture.md#103-initial-alert-set).
- **Cost:** a monthly budget (`HH_BUDGET_AMOUNT`, 100 by default) with e-mail alerts at 80 % spent and 100 % forecast, declared in `infra/budget.bicep`. `provision.sh deploy` deploys it separately and only warns if the subscription's offer does not support budgets; costs are then watched in Cost analysis by hand. Rehearsal resource groups are deleted after use (§2).

## 8. Open Items

- [ ] **Region capacity:** confirm that the subscription can create the VM size ([container-deployment.md §3.1](container-deployment.md#31-the-vm)) and PostgreSQL Flexible Server in Germany West Central; `provision.sh check` reports both.
- [ ] **First real apply:** the templates compile and lint cleanly, but have not been deployed yet; expect fixes on the first run.
- [ ] **First provisioning:** run §4.1 in a rehearsal resource group end to end, then in production.
- [ ] **Rebuild drill:** replace the VM (§4.3) and record how long it takes, as input to the RTO in `backup-recovery-plan.md`.
