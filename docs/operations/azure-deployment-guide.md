# Deploying HealthyHires to Azure

A step-by-step guide from an empty Azure subscription to the application running on `https://<label>.germanywestcentral.cloudapp.azure.com`, and how to move it to a teammate's subscription when a student credit runs out.

**Audience:** the operator — whoever owns the Azure subscription the application runs in.

**How it fits together:** the architecture is described in [infrastructure-operations-architecture.md](../architecture/infrastructure-operations-architecture.md), [container-deployment.md](../architecture/container-deployment.md) and [iam.md](../architecture/iam.md). This guide only says what to do, in order.

**Time:** about two hours the first time, most of it waiting for Azure. Moving to another subscription later takes under an hour (§11).

## 0. What Is Where

Everything that differs between people or subscriptions lives in **one gitignored file**, `infra/azure.env`. Everything else is in the repository and identical for everyone. That is what makes a move to another subscription a matter of a new `azure.env` and the same commands.

| Piece | Lives in | Changes when the subscription changes? |
| ----- | -------- | -------------------------------------- |
| Azure resources | `infra/` (Bicep, [ADR-023](../architecture/ADR.md#adr-023-infrastructure-as-code--bicep)) | Recreated from the same templates |
| VM first-boot setup and host scripts | `infra/cloud-init.yaml`, `deploy/host/` | No |
| Containers, Collector, Falco rules | `deploy/quadlets/`, `deploy/otelcol/`, `deploy/falco/` | No |
| Build, sign and deploy pipeline | `.github/workflows/deploy.yml` | No |
| Container images | GitHub Container Registry | No |
| Deploy settings (hostnames, endpoints, keys) | GitHub environment `production` | Yes — rewritten by `provision.sh github` |
| Staff sign-in (app registrations, users) | The company Entra tenant | No |
| Admin network | NetBird account | Only the VM's peer |
| Per-person settings | `infra/azure.env` | Yes — one per subscription |

```text
 your machine                           GitHub                         Azure subscription
 ────────────                           ──────                         ──────────────────
 infra/azure.env ──provision.sh──▶ Bicep ───────────────────────────▶ VM, PostgreSQL, Storage,
                 └──provision.sh github──▶ environment "production"     Key Vault, ACS, Monitor;
                                                                        Azure OpenAI (Sweden Central)
 git push ─────────────────────────▶ deploy.yml: test, build, scan,
                                     sign, push to GHCR ──NetBird SSH──▶ VM: hh-deploy verifies
                                                                        signatures, starts Quadlets
```

## 1. Prerequisites

### 1.1 Accounts

- [ ] **An Azure subscription** where you are Owner — for example Azure for Students. Note its credit and end date on the [Azure for Students](https://portal.azure.com/#view/Microsoft_Azure_Education/EducationMenuBlade/~/overview) page.
- [ ] **A GitHub repository** for the code, under your account or a team organisation.
- [ ] **A NetBird account** (free tier) for the admin network ([ADR-014](../architecture/ADR.md#adr-014-admin-access--netbird-operator-and-ci-runner-ssh)).
- [ ] **The company Entra tenant** for staff sign-in — the free tenant with the pre-created staff accounts. It can be a different tenant from the one that holds the Azure subscription.

### 1.2 Tools on your machine

| Tool | Used for | Check |
| ---- | -------- | ----- |
| Azure CLI | Everything in Azure | `az version` |
| GitHub CLI | Setting the deploy environment | `gh auth status` |
| `jq`, `ssh`, `ssh-keygen` | The scripts | `jq --version` |
| NetBird client | Reaching the VM | `netbird status` |
| `psql`, `pg_dump`, `pg_restore` **18 or newer** | Database grant and migration | `psql --version` |
| `just` (optional) | Shorter commands | `just --list` |

Have an SSH key of your own (`~/.ssh/id_ed25519.pub`); create one with `ssh-keygen -t ed25519` if not.

### 1.3 What it costs

Approximate pay-as-you-go prices in Germany West Central (Azure OpenAI: Sweden Central). Check the real numbers in **Cost Management → Cost analysis** after the first few days.

| Resource | Approx. per month | Notes |
| -------- | ----------------- | ----- |
| VM `Standard_B2s` + 64 GB Standard SSD | €35–40 | ClamAV needs the 4 GB of RAM; a B1s is too small |
| PostgreSQL `Standard_B1ms` + 32 GB | €15–20 | Can be free for 12 months — see **Free services** in the portal |
| Standard public IP | €3–4 | Kept while the VM is stopped |
| Availability test (1 location, every 15 min) | €3–4 | Billed per run; 15 minutes is the longest interval Azure allows |
| Log alert rules (8) | €3–6 | Billed per rule |
| Log Analytics / Application Insights | €0 | Inside the free 5 GB/month at this traffic |
| Storage, Key Vault, ACS Email | < €1 |  |
| Azure OpenAI `gpt-4o`, Standard deployment | €0 idle, ≈ €0.02–0.03 per scored CV | Billed per token: €0.0027 per 1,000 input and €0.0106 per 1,000 output tokens; 100 CVs ≈ €2–3 ([ADR-024](../architecture/ADR.md#adr-024-cv-scoring--azure-openai-gpt-4o-sweden-central)) |
| **Total** | **≈ €55–70** + CV scoring | A €100 student credit lasts about seven to eight weeks if it runs 24/7 |

`HH_BUDGET_AMOUNT` in `azure.env` (100 by default, in the subscription's billing currency) creates a monthly budget that e-mails you at 80 % spent and when the forecast passes 100 %. Some student offers do not support budgets; `just azure-deploy` then prints a warning, and you watch Cost analysis yourself.

To stretch the credit, pause the deployment between demos with `just azure-pause` (§10.3).

## 2. Put the Code on GitHub

```bash
git init -b main
```

```bash
git add . && git commit -m "Initial commit"
```

```bash
gh repo create <owner>/healthyhires --private --source . --push
```

In the repository's **Settings → Actions → General**, allow GitHub Actions. The `deploy` workflow runs on every push to `main`; it will fail at the deploy step until §8 is done, which is expected.

## 3. Fill In `infra/azure.env`

```bash
cp infra/azure.env.example infra/azure.env
```

```bash
az login
```

```bash
az account list -o table
```

Fill in:

| Setting | Value |
| ------- | ----- |
| `HH_SUBSCRIPTION_ID` | The subscription id from the table above |
| `HH_OPERATOR_OBJECT_ID` | `az ad signed-in-user show --query id -o tsv` |
| `HH_OPERATOR_UPN` | `az ad signed-in-user show --query userPrincipalName -o tsv` |
| `HH_OPERATOR_SSH_PUBLIC_KEY_FILE` | Your SSH public key |
| `HH_GITHUB_REPOSITORY` | `<owner>/healthyhires` |
| `HH_ALERT_EMAIL` | Where alerts go |
| `HH_DNS_LABEL` | The first part of the hostname, e.g. `healthyhires`. Must be free in the region |
| `HH_BUDGET_AMOUNT` | Monthly budget for the e-mail cost alerts (§1.3); `0` for none |

The Entra values come in §4. The file is gitignored: it is not secret, but it is yours.

## 4. Register the App in the Company Entra Tenant

This happens in the **company** tenant ([iam.md §2.1](../architecture/iam.md#21-app-registrations)), once. It does not depend on the Azure subscription, so it survives every move in §11 as long as the hostname stays the same.

In the [Entra admin center](https://entra.microsoft.com), signed in to the company tenant:

1. **HealthyHires API** — *App registrations → New registration*, single tenant, no redirect URI.
   - *Expose an API* → set the Application ID URI to `api://<api-client-id>` → add the scope `access_as_user` (admins and users can consent).
   - *App roles* → create `Recruiter`, `HiringManager` and `Admin` (the **Value**; display names *Recruiter*, *Hiring Manager*, *Administrator*), allowed member type *Users/Groups*.
2. **HealthyHires SPA** — *New registration*, single tenant, redirect URI of type **Single-page application**: `https://<label>.germanywestcentral.cloudapp.azure.com`. For local development, also add `http://localhost:5173`.
   - *API permissions* → *Add* → *My APIs* → HealthyHires API → `access_as_user` → *Grant admin consent*.
3. *Enterprise applications* → each of the two → *Properties* → **Assignment required = Yes**.
4. *Enterprise applications* → HealthyHires API → *Users and groups* → assign each staff user an app role.

Then add to `infra/azure.env`:

| Setting | Value |
| ------- | ----- |
| `HH_ENTRA_TENANT_ID` | The company tenant's id (*Overview*) |
| `HH_ENTRA_CLIENT_ID` | The **SPA**'s Application (client) id |
| `HH_API_SCOPE` | `api://<api-client-id>/access_as_user` |

The API validates tokens against the id inside `HH_API_SCOPE`, so keep the Application ID URI in the default `api://<api-client-id>` form; with any other form the API refuses to start.

## 5. Set Up NetBird

In the [NetBird dashboard](https://app.netbird.io):

1. **Groups:** create `hh-servers`, `hh-ci` and `hh-operators`.
2. **Access control:** remove the default *All → All* policy, then add two policies, both TCP port 22:
   - `hh-operators` → `hh-servers`
   - `hh-ci` → `hh-servers`
3. **Your machine:** install the NetBird client, run `netbird up`, and put your peer in `hh-operators`.
4. **CI setup key:** *Setup keys → Create* — **reusable**, **ephemeral**, auto-assigned group `hh-ci`, expiry as long as the project lasts. Keep it for §8.
5. **VM setup key:** create it later, in §7, right before it is used.

## 6. Provision the Azure Resources

```bash
just azure-check
```

The check registers the resource providers a new subscription lacks (including `Microsoft.CognitiveServices` for Azure OpenAI), and confirms that the region accepts the VM size, that the vCPU quota is enough (the VM needs 2) and that the AlmaLinux 10 image exists. It also creates the CI deploy key in `~/.config/healthyhires/<subscription-id>/`. Fix anything it reports before going on; §12 lists the usual causes.

```bash
just azure-what-if
```

Read the list: on a new subscription everything is a *Create*. Then:

```bash
just azure-deploy
```

It takes 10–15 minutes and prints the app hostname, VM name, PostgreSQL host and Key Vault URI. Azure OpenAI is created in Sweden Central even though everything else is in Germany West Central: it is the only allowed region where the student subscription has quota for a Standard deployment. Without `just`, the same commands are `scripts/azure/provision.sh check|what-if|deploy`.

From this point Azure Monitor e-mails availability alerts, because nothing answers on the hostname yet. They stop after §8.

## 7. Join the VM to NetBird

In the NetBird dashboard, create a **one-off** setup key: not reusable, expiry 1 hour, auto-assigned group `hh-servers`. Then:

```bash
just azure-netbird
```

Paste the key at the prompt. It reaches the VM once through Azure Run Command and is not stored anywhere. Check that you can log in:

```bash
ssh azureops@<vm-netbird-ip>
```

The NetBird IP is in the command's output and in the dashboard. SSH from the internet does not work, by design.

Then set the **break-glass password** for Azure Serial Console, the way in when NetBird or the network is broken:

```bash
just azure-breakglass
```

It generates a random password for the local `breakglass` user, stores it **only** in Key Vault as `breakglass-password`, and sends just its hash to the VM. The user cannot log in over SSH. Run it again whenever the password should change, and after a VM rebuild.

## 8. Connect GitHub to the Deployment

```bash
just azure-github
```

This writes the deployment's outputs to the GitHub environment `production` — hostname, Blob endpoint, PostgreSQL host and login role, Key Vault URI, Azure OpenAI endpoint and deployment name, the Entra values, and the VM's NetBird address and SSH host key — and sets the deploy key as the secret `DEPLOY_SSH_KEY`. Then add the CI setup key from §5:

```bash
gh secret set NETBIRD_SETUP_KEY --repo <owner>/healthyhires --env production
```

## 9. Grant the Database Login and Deploy

### 9.1 Database login

```bash
just azure-db-grant
```

It opens an SSH tunnel to PostgreSQL through the VM, signs in with your Entra token, creates the group role `healthyhires_app` and the login role for the VM's managed identity, and drops login roles of earlier VMs. The API never has a password.

### 9.2 First deploy

```bash
gh workflow run deploy --repo <owner>/healthyhires
```

The **first** run creates the two container packages in GitHub Container Registry as private, so the VM cannot pull them and the deploy step fails. Make both public — they contain no secrets — and re-run:

1. GitHub → your profile or organisation → *Packages* → `healthyhires-api` → *Package settings* → *Change visibility* → **Public**. The same for `healthyhires-caddy`.
2. In the failed workflow run → *Re-run failed jobs*.

Every later push to `main` deploys by itself.

### 9.3 Check that it works

Work through the readiness checks in [infrastructure-operations-architecture.md §4.2](../architecture/infrastructure-operations-architecture.md#42-readiness-checks). The quick ones:

```bash
curl -fsS https://<label>.germanywestcentral.cloudapp.azure.com/health
```

```bash
nc -zv -w 5 <label>.germanywestcentral.cloudapp.azure.com 22
```

The first prints `Healthy`; the second must time out. On the VM:

```bash
ssh azureops@<vm-netbird-ip> 'sudo systemctl --user -M healthyhires@ list-units "*.service" --no-pager; sudo systemctl status falco --no-pager'
```

Initialise the AIDE baseline now that the release is installed, and run the first OpenSCAP scan instead of waiting for the night:

```bash
ssh azureops@<vm-netbird-ip> 'sudo aide --init && sudo mv /var/lib/aide/aide.db.new.gz /var/lib/aide/aide.db.gz && sudo systemctl start hh-openscap'
```

Test the break-glass path once: *Virtual machine → Help → Serial console*, log in as `breakglass` with the password from `az keyvault secret show --vault-name <key-vault> --name breakglass-password --query value -o tsv`, then `exit`.

In the portal, *Application Insights → Logs*: `AppRequests | take 10` shows API requests, and `AppTraces | summarize count() by AppRoleName` shows `falco`, `caddy`, `clamav` and `host` within a few minutes.

## 10. Day-to-Day Operation

### 10.1 Changes

| Change | How |
| ------ | --- |
| Application code | Push to `main`; the workflow tests, builds, signs and deploys |
| Roll back the application | `ssh azureops@<vm> sudo /usr/local/sbin/hh-deploy rollback` |
| Azure resources (`infra/`) | Pull request, then `just azure-what-if` and `just azure-deploy` |
| Quadlets, Collector or Falco config (`deploy/`) | Push to `main`, like code |
| `infra/cloud-init.yaml` or `deploy/host/` | Needs a rebuilt VM (§10.2): Azure does not let custom data change |
| A new backend module with its own schema | Add the schema to `infra/db-schemas.txt`, run `just azure-db-grant`, then push. The API cannot create schemas itself |
| Accept a Trivy or ZAP finding | Only with a reason: `.trivyignore` or `.zap/rules.tsv`. Prefer fixing it |

### 10.2 Rebuilding the VM

The VM holds no business data, so it is replaced rather than repaired ([infrastructure-operations-architecture.md §4.3](../architecture/infrastructure-operations-architecture.md#43-rebuild-and-replacement)). The public IP, and with it the hostname, survives.

```bash
az vm delete --resource-group rg-healthyhires-prod --name vm-healthyhires-prod --yes
```

Remove the old peer in the NetBird dashboard, then repeat §6 (`just azure-deploy`), §7 (including `just azure-breakglass`), §8 (`just azure-github`), §9.1 and a deploy (`gh workflow run deploy`). The new VM has a new managed identity, which is why the database grant has to run again.

### 10.3 Pausing Between Demos

The application only has to run for demos, so pause it in between. Pausing keeps all data, the public IP and the hostname, the NetBird enrolment and the TLS certificate. Resuming takes about 10 minutes and needs nothing else.

```bash
just azure-pause
```

This turns off the availability test and the alert rules, deallocates the VM and stops PostgreSQL. Before a demo:

```bash
just azure-resume
```

This starts PostgreSQL, then the VM, and waits up to 10 minutes for `/health`. Only then does it turn the test and the alerts back on. To see what is running at any time:

```bash
just azure-status
```

Without `just`: `scripts/azure/power.sh pause|resume|status`. Every step is safe to repeat.

| | Running | Paused |
| - | ------- | ------ |
| Cost per month | ≈ €55–70 (§1.3) | ≈ €10–15: VM disk, public IP, PostgreSQL storage, Storage and Key Vault |
| Data, hostname, certificate, NetBird | Kept | Kept |
| Alerts | On | Off, except the budget and the role/NSG/lock change alert |

**Caveats:**

- **PostgreSQL starts again by itself after 7 days.** Azure does not let a flexible server stay stopped for longer and does not ask first. The server then runs, and costs about €0.50 a day, while the VM is still off. `just azure-pause` prints the date this will happen. Run `just azure-pause` again before then, for example from a calendar reminder; it only stops PostgreSQL if it is running. `just azure-status` warns when it has happened.
- **Alerts fire if you stop things any other way.** The alerts are built to notice the VM being down. If you stop the VM or PostgreSQL from the portal or with `az` directly, these keep e-mailing until it is back: *availability* (`/health` every 15 minutes), *collector-silent* and *falco-heartbeat* (all Sev 1), and *host-report-missing* after two days. Pause and resume with the commands above.
- **`just azure-deploy` turns monitoring back on.** The Bicep templates set the availability test and every alert to enabled. Run `just azure-pause` again after deploying infrastructure changes while paused.
- **Pushes to `main` do not deploy while paused.** The workflow builds and signs the images, then fails at the deploy step because the VM cannot be reached over NetBird. After resuming, re-run the failed job or `gh workflow run deploy`.
- **Resume well before the demo.** On its first boot after a pause, the VM catches up on the nightly jobs it missed (OpenSCAP, AIDE and the automatic updates; the timers are `Persistent`), so it is slow for several minutes after `/health` turns green. Resume about 30 minutes ahead.
- **Long pauses and the certificate.** Let's Encrypt certificates last 90 days, and Caddy renews them 30 days early, which only happens while it runs. After a pause of more than about two months, the certificate may have expired. Caddy renews it within a minute or two of starting, as long as port 80 is reachable; until then the browser shows a certificate error.
- **Nothing else to stop.** Azure OpenAI and ACS Email cost nothing while idle. Log Analytics only bills for data that arrives.

**If the next demo is weeks away**, removing the resource group altogether brings the cost to zero. Bringing it back is a full §6–§9 run plus a data import, about an hour:

1. Export the data: `scripts/azure/migrate.sh export ./migration` (§11.2).
2. Delete the resource group: the lock removal and `az group delete` in §11.3.
3. Before provisioning again, purge the soft-deleted Key Vault and Azure OpenAI account so their names are free: `az keyvault purge --name <key-vault>` and `az cognitiveservices account purge --name <openai-account> --resource-group rg-healthyhires-prod --location swedencentral`. With purge protection on (the default), the Key Vault **cannot** be purged and its name stays taken for 7 days. For a deployment you plan to tear down, set `HH_ENABLE_PURGE_PROTECTION=false` and `HH_ENABLE_LOCKS=false` in `azure.env` before the first `just azure-deploy`.
4. Provision and import as in §11.4–§11.5, on the same subscription.

## 11. Moving to a Teammate's Subscription

When the credit runs out, the deployment moves to another subscription with the same templates. The GitHub repository, the container images, the Entra app registrations and the NetBird account stay as they are.

### 11.1 Decide who operates the new subscription

Either works, because everything person-specific is in `azure.env`:

- **You keep operating it:** the teammate invites you to their subscription as **Owner** (*Subscriptions → Access control → Add role assignment*). You then sign in to their tenant with `az login --tenant <their-tenant-id>`.
- **The teammate operates it:** they clone the repository, do §1.2, and follow this section with their own `azure.env`.

### 11.2 Export from the old subscription

Keep the old settings under a second name, so both subscriptions can be addressed:

```bash
cp infra/azure.env infra/azure.old.env
```

```bash
HH_ENV_FILE=infra/azure.old.env scripts/azure/migrate.sh export ./migration
```

This dumps the database and downloads every blob container into `./migration` (gitignored). It holds applicants' personal data: keep it on an encrypted disk and delete it after §11.5. Check that `migration/healthyhires.dump` exists and is not empty before going on.

### 11.3 Free the hostname

A DNS label is unique per region. To keep the same URL — so the Entra redirect URI and every bookmark stay valid — delete the old resource group, which removes its public IP. Its delete locks have to go first:

```bash
az account set --subscription <old-subscription-id>
```

```bash
az lock list --resource-group rg-healthyhires-prod --query '[].id' -o tsv | xargs -r -n1 az lock delete --ids
```

```bash
az group delete --name rg-healthyhires-prod
```

**This deletes the old database and blobs.** Run it only after §11.2 has succeeded. If the old subscription has already been disabled, its resources are unreachable anyway; in that case either wait until Azure deletes them or choose a new `HH_DNS_LABEL` and add the new redirect URI in Entra (§4).

Also remove the old VM's peer in the NetBird dashboard.

### 11.4 Provision the new subscription

Edit `infra/azure.env` for the new subscription: `HH_SUBSCRIPTION_ID`, and `HH_OPERATOR_OBJECT_ID` and `HH_OPERATOR_UPN` as `az ad signed-in-user show` reports them **in the new subscription's tenant** — a guest account has a different object id there. Then, in order:

1. §6 — `just azure-check`, `just azure-what-if`, `just azure-deploy`
2. §7 — a new one-off NetBird key, `just azure-netbird`, then `just azure-breakglass`
3. §8 — `just azure-github` (overwrites the GitHub environment, including a new deploy key)
4. §9.1 — `just azure-db-grant`

### 11.5 Import and deploy

Import **before** the first deploy, so the API starts against the restored schema:

```bash
scripts/azure/migrate.sh import ./migration
```

```bash
gh workflow run deploy --repo <owner>/healthyhires
```

Run the checks in §9.3, including the AIDE baseline. When everything works, delete `./migration` and `infra/azure.old.env`.

## 12. Troubleshooting

| Symptom | Cause and fix |
| ------- | ------------- |
| `azure-check`: VM size restricted, or quota too low | Student subscriptions allow only some sizes and regions. Try `HH_VM_SIZE=Standard_B2ms`, or another EU region in `HH_LOCATION`, or request a quota increase under *Quotas* |
| Deployment fails with `RequestDisallowedByPolicy` | The region is outside the subscription's own allowed-regions policy or outside HealthyHires' EU list in `infra/modules/policy.bicep`. `az policy assignment list -o table` shows the subscription's policy; choose a region both allow, or adjust `policy.bicep` if the subscription allows other EU regions |
| Deployment fails with `ServiceModelDeprecated` | The model version in `infra/modules/openai.bicep` no longer accepts new deployments. List what can still be deployed with `az cognitiveservices model list -l swedencentral -o table` (check each SKU's `deprecationDate`) and set `modelName` / `modelVersion` |
| Deployment fails with `InsufficientQuota` on the model deployment | No quota for that model and deployment type in the region. `az cognitiveservices usage list -l swedencentral -o table` shows the limits; lower `capacity` or request quota in the Azure AI Foundry portal |
| Deployment fails on the image | Run `az vm image list -p almalinux --all -l <region> -o table` and set `HH_IMAGE_SKU` / `HH_IMAGE_OFFER` to what exists |
| PostgreSQL: version not supported | Set `HH_POSTGRES_VERSION=17` and deploy again; the local stack runs 18, so check migrations against 17 too |
| Deployment fails: `customData` cannot be changed | `infra/cloud-init.yaml` or `deploy/host/` changed. Rebuild the VM (§10.2) |
| `azure-netbird` succeeds but SSH times out | The NetBird access policy (§5) does not allow your group to `hh-servers` on TCP 22, or your peer is not in `hh-operators` |
| Deploy workflow: `Permission denied (publickey)` | The GitHub environment is out of date after a rebuild; run `just azure-github` again |
| Deploy workflow: `signature verification failed` | The packages are still private (§9.2), or the run was not from `main` |
| `/health` returns `502` | The API is not running: `ssh azureops@<vm> 'sudo journalctl _SYSTEMD_USER_UNIT=api.service -n 50'`. A PostgreSQL login error means §9.1 was not run for this VM |
| `/health` returns `503` | PostgreSQL or Blob Storage unreachable — PostgreSQL stopped (§10.3), or the role grant missing |
| Availability or *collector-silent* alerts while the app is paused | Something was stopped outside `just azure-pause`, or `just azure-deploy` turned monitoring back on. Run `just azure-pause` (§10.3) |
| `just azure-resume` stops with `/health still fails` | Alerts stay off. Find the cause with the `/health` rows above, then run `just azure-resume` again |
| No telemetry in Application Insights | `ssh azureops@<vm> 'sudo journalctl _SYSTEMD_USER_UNIT=otelcol.service -n 50'`; a Key Vault error means the managed identity cannot read the connection-string secret |
| `azure-deploy` warns that Azure refused the budget | The offer does not support Cost Management budgets. Everything else was deployed; watch *Cost analysis* yourself |
| `deploy` or `ci` fails in a Trivy step | A Critical or High vulnerability in an image or npm package. Update the dependency or base image; only if no fix exists, add it to `.trivyignore` with a reason |
| `ci` fails in the ZAP step | A new ZAP warning. Fix the header or response; only an accepted finding goes into `.zap/rules.tsv`, with its reason |
| The API logs `permission denied for schema` or `for database` | The module's schema is missing from `infra/db-schemas.txt`, or `just azure-db-grant` was not run after adding it |
| The browser shows a certificate error right after the first deploy | Caddy is still getting the Let's Encrypt certificate; wait a minute. Port 80 must be reachable for it |
