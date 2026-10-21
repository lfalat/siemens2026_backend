set windows-shell := ["C:/Program Files/Git/bin/bash.exe", "-cu"]

# Task runner for local development (docs/architecture/development-architecture.md §3). It only wraps
# the documented commands: the backend and the frontend still start on their own, so neither run depends
# on the other.

root := justfile_directory()

default:
  @just --list

# Starts the API with PostgreSQL, Azurite, Mailpit and ClamAV, and opens the Aspire dashboard.
aspire-up:
  dotnet run --project AppHost

# Starts the Vite dev server; it proxies /api and /hubs to API_URL (default http://localhost:5180).
app:
  npm run dev --prefix App

# Builds the frontend into App/dist, which the edge serves.
app-build:
  npm run build --prefix App

# Needs `just app-build` and a running API. Stop it with Ctrl+C; --rm removes the container.
# Starts Caddy on http://localhost:8088 with the production routes and headers.
edge:
  docker run --rm --name healthyhires-edge --network host \
    -v "{{root}}/configs/caddy/Caddyfile:/etc/caddy/Caddyfile:ro" \
    -v "{{root}}/configs_production/site.caddy:/etc/caddy/site.caddy:ro" \
    -v "{{root}}/App/dist:/srv:ro" \
    -e API_UPSTREAM=localhost:5180 \
    -e BLOB_ENDPOINT=http://localhost:10000 \
    caddy:2.11.7

# Builds the backend solution.
build:
  dotnet build Api/Api.slnx

# Runs the backend tests; integration tests start their own containers through Testcontainers.
test:
  dotnet test --solution Api/Api.slnx

# For whoever already has the test users: writes them to e2e-users.json, to hand to a teammate privately.
e2e-export:
  scripts/e2e.sh export

# Installs the browser, then imports the test users from e2e-users.json (or asks for them).
# One-time setup for the end-to-end tests.
e2e-setup:
  scripts/e2e.sh setup

# Needs `just aspire-up` and `just app` running; checks both first. Extra arguments go to dotnet test.
# Runs the browser end-to-end tests (docs/testing/e2e.md). HEADED=1 shows the browser.
e2e *args:
  scripts/e2e.sh run {{args}}

# The AppHost keeps ClamAV's signatures in a named volume so it does not download them on every run;
# the next run starts from the signatures bundled in the image. Stop the AppHost first.
# Removes the ClamAV signature volume.
clamav-reset:
  docker volume rm healthyhires-clamav-db || true

# Re-renders the PlantUML diagrams in docs/diagrams/architecture to SVG.
diagrams:
  scripts/render-diagrams.sh

# Normalizes Markdown table padding in a file or directory (default: docs/).
md-tables path="docs":
  python3 scripts/compress_markdown_tables.py {{path}}

# Azure (docs/operations/azure-deployment-guide.md). Settings come from infra/azure.env.

# Checks the subscription: providers, region, VM size, quota and image; creates the CI deploy key.
azure-check:
  scripts/azure/provision.sh check

# Shows what `just azure-deploy` would change, without changing anything.
azure-what-if:
  scripts/azure/provision.sh what-if

# Creates or updates every Azure resource from infra/main.bicep.
azure-deploy:
  scripts/azure/provision.sh deploy

# Copies the deployment outputs into the GitHub "production" environment the deploy workflow reads.
azure-github:
  scripts/azure/provision.sh github

# Joins the VM to NetBird with a one-off setup key typed at the prompt.
azure-netbird:
  scripts/azure/enrol-netbird.sh

# Sets a new Serial Console break-glass password; its only copy is stored in Key Vault.
azure-breakglass:
  scripts/azure/provision.sh breakglass

# Gives the VM's managed identity its PostgreSQL login; again after every VM rebuild.
azure-db-grant:
  scripts/azure/db-grant-identity.sh

# Creates the tables in a new Azure database from configs/postgres/init/01-create-db.sql; once, after azure-db-grant.
azure-db-schema:
  scripts/azure/db-schema.sh

# Gives the VM's managed identity the Graph permissions for creating staff accounts; again after every VM rebuild.
azure-graph-grant:
  scripts/azure/graph-grant-identity.sh

# Pauses the deployment between demos: alerts off, VM deallocated, PostgreSQL stopped. Again within 7 days.
azure-pause:
  scripts/azure/power.sh pause

# Starts PostgreSQL and the VM, waits for /health, then turns the alerts back on.
azure-resume:
  scripts/azure/power.sh resume

# Shows whether the VM and PostgreSQL run, and warns if PostgreSQL has started by itself.
azure-status:
  scripts/azure/power.sh status
