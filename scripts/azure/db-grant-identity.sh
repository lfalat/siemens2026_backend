#!/usr/bin/env bash
# Gives the VM's managed identity its PostgreSQL login (iam.md §4.2). Run after the first provisioning and after
# every VM rebuild, because a rebuilt VM gets a new identity (infrastructure-operations-architecture.md §4.3).
#
# Objects are owned by the NOLOGIN group role healthyhires_app; the API's login role is only a member of it and
# switches to it on connect (Options=-c role=healthyhires_app in deploy/render-bundle.sh). A rebuild therefore
# swaps the login role without touching ownership, and the old one is dropped.
#
# Access is per module schema (iam.md §4.2): the schemas listed in infra/db-schemas.txt are created here and owned
# by healthyhires_app, which may connect to the database but not create further schemas of its own.
#
# PostgreSQL has no public access, so the connection runs through an SSH tunnel over NetBird; the password is
# the operator's Entra token. Needs psql 18+ locally and this machine on NetBird.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env
need psql ssh

pg_host=$(output postgresHost)
principal=$(output vmPrincipalId)
role="hh-api-${principal:0:8}"
vm_host=${HH_VM_SSH_HOST:-$(run_on_vm 'netbird status --json | jq -r .netbirdIp' | cut -d/ -f1 | tr -d '[:space:]')}
port=${HH_TUNNEL_PORT:-6432}

log "Opening a tunnel to $pg_host through azureops@$vm_host"
ssh -f -N -o ExitOnForwardFailure=yes -L "$port:$pg_host:5432" "azureops@$vm_host"
trap 'pkill -f "ssh -f -N -o ExitOnForwardFailure=yes -L $port:$pg_host:5432" || true' EXIT
sleep 1

PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
export PGPASSWORD PGSSLMODE=require

admin() { psql "host=localhost port=$port user=$HH_OPERATOR_UPN dbname=$1" -v ON_ERROR_STOP=1 -q "${@:2}"; }

log "Creating roles"
admin postgres <<SQL
DO \$\$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'healthyhires_app') THEN
        CREATE ROLE healthyhires_app NOLOGIN;
    END IF;
END \$\$;
GRANT CONNECT, TEMPORARY ON DATABASE healthyhires TO healthyhires_app;
REVOKE CREATE ON DATABASE healthyhires FROM healthyhires_app;
-- The operator can act as the application role, which migrate.sh needs for pg_dump and pg_restore.
GRANT healthyhires_app TO "$HH_OPERATOR_UPN";

SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$role') THEN 'exists'
       ELSE (SELECT 'created' FROM pgaadauth_create_principal_with_oid('$role', '$principal', 'service', false, false)) END;
GRANT healthyhires_app TO "$role";
SQL

log "Module schemas from infra/db-schemas.txt"
while IFS= read -r schema; do
    [[ -z $schema || $schema == \#* ]] && continue
    [[ $schema =~ ^[a-z][a-z0-9_]*$ ]] || die "invalid schema name in infra/db-schemas.txt: $schema"
    admin healthyhires -c "CREATE SCHEMA IF NOT EXISTS $schema AUTHORIZATION healthyhires_app"
    log "  $schema"
done < "$root/infra/db-schemas.txt"

log "Dropping login roles of earlier VMs"
for old in $(admin postgres -At -c "SELECT rolname FROM pg_roles WHERE rolname LIKE 'hh-api-%' AND rolname <> '$role'"); do
    if admin postgres -c "DROP ROLE \"$old\""; then
        log "dropped $old"
    else
        warn "could not drop $old; it can no longer log in (its identity is gone), drop it later by hand"
    fi
done

log "Done: the API logs in as $role. The GitHub variable PG_USER must be $role (provision.sh github sets it)."
