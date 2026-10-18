#!/usr/bin/env bash
# Builds the release bundle that deploy/host/hh-deploy installs on the VM: the Quadlet units with the image
# digests filled in, the environment files, the Collector configuration and the Falco rules. Run by the deploy
# workflow; every value comes from the environment, i.e. from the GitHub "production" environment's variables,
# which scripts/azure/provision.sh sets.
#
#   deploy/render-bundle.sh <output.tgz>
set -euo pipefail

out=${1:?usage: render-bundle.sh <output.tgz>}
root=$(cd "$(dirname "$0")/.." && pwd)

required=(RELEASE_ID API_IMAGE CADDY_IMAGE APP_HOST ACME_EMAIL BLOB_ENDPOINT PG_HOST PG_USER KEY_VAULT_URI
    ENTRA_TENANT_ID ENTRA_CLIENT_ID API_SCOPE)
for name in "${required[@]}"; do
    [[ -n "${!name:-}" ]] || { echo "render-bundle: $name is not set" >&2; exit 1; }
done
for image in "$API_IMAGE" "$CADDY_IMAGE"; do
    [[ $image == *@sha256:* ]] || { echo "render-bundle: not pinned by digest: $image" >&2; exit 1; }
done

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work"/{env,quadlets/user,quadlets/system,otelcol,falco}

printf 'RELEASE_ID=%s\n' "$RELEASE_ID" > "$work/release.env"

for file in "$root"/deploy/quadlets/user/* "$root"/deploy/quadlets/system/*; do
    target="$work/quadlets/$(basename "$(dirname "$file")")/$(basename "$file")"
    sed -e "s|@API_IMAGE@|$API_IMAGE|g" -e "s|@CADDY_IMAGE@|$CADDY_IMAGE|g" "$file" > "$target"
done
cp "$root/deploy/otelcol/config.yaml" "$work/otelcol/"
cp "$root"/deploy/falco/*.yaml "$work/falco/"

# The API validates staff tokens against the API registration, whose id is the middle of API_SCOPE
# (api://<api-client-id>/access_as_user); ENTRA_CLIENT_ID is the SPA's, which the API never sees.
api_client_id=${API_SCOPE#api://}
api_client_id=${api_client_id%%/*}

# The API connects with its managed identity: no password, so Aspire.Azure.Npgsql uses an Entra token, and the
# session runs as the healthyhires_app group role that owns the schemas (scripts/azure/db-grant-identity.sh).
cat > "$work/env/api.env" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://localhost:8080
ConnectionStrings__healthyhires=Host=$PG_HOST;Database=healthyhires;Username=$PG_USER;SSL Mode=Require;Options=-c role=healthyhires_app
ConnectionStrings__blobs=$BLOB_ENDPOINT
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_SERVICE_NAME=healthyhires-api
OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production,service.version=$RELEASE_ID
Entra__TenantId=$ENTRA_TENANT_ID
Entra__ApiClientId=$api_client_id
EOF

cat > "$work/env/caddy.env" <<EOF
APP_HOST=$APP_HOST
ACME_EMAIL=$ACME_EMAIL
BLOB_ENDPOINT=${BLOB_ENDPOINT%/}
ENTRA_TENANT_ID=$ENTRA_TENANT_ID
ENTRA_CLIENT_ID=$ENTRA_CLIENT_ID
API_SCOPE=$API_SCOPE
EOF

cat > "$work/env/host.env" <<EOF
KEY_VAULT_URI=$KEY_VAULT_URI
EOF

tar -czf "$out" -C "$work" .
echo "render-bundle: wrote $out for release $RELEASE_ID"
