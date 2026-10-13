#!/usr/bin/env bash
# Moves the data to another subscription, e.g. when a student subscription's credit runs out (guide §11).
#
#   scripts/azure/migrate.sh export <dir>   with infra/azure.env pointing at the OLD subscription
#   scripts/azure/migrate.sh import <dir>   with infra/azure.env pointing at the NEW subscription
#
# Exports the database (pg_dump, custom format) and every blob container to <dir>. The VM holds no business data
# (container-deployment.md §1.0), so nothing else needs to move. <dir> then holds applicants' personal data:
# keep it on an encrypted disk and delete it once the import is verified.
#
# Uses HH_ENV_FILE to choose the settings file, so both subscriptions can be configured side by side:
#   HH_ENV_FILE=infra/azure.old.env scripts/azure/migrate.sh export ./migration
set -euo pipefail
source "$(dirname "$0")/lib.sh"

action=${1:-}
dir=${2:-}
[[ $action =~ ^(export|import)$ && -n $dir ]] || die "usage: migrate.sh export|import <dir>"

load_env
need pg_dump pg_restore ssh

pg_host=$(output postgresHost)
account=$(output storageAccountName)
vm_host=${HH_VM_SSH_HOST:-$(run_on_vm 'netbird status --json | jq -r .netbirdIp' | cut -d/ -f1 | tr -d '[:space:]')}
port=${HH_TUNNEL_PORT:-6432}

ssh -f -N -o ExitOnForwardFailure=yes -L "$port:$pg_host:5432" "azureops@$vm_host"
trap 'pkill -f "ssh -f -N -o ExitOnForwardFailure=yes -L $port:$pg_host:5432" || true' EXIT
sleep 1
PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
export PGPASSWORD PGSSLMODE=require
conn="host=localhost port=$port user=$HH_OPERATOR_UPN dbname=healthyhires"

case $action in
    export)
        install -d -m 700 "$dir/blobs"
        log "Dumping the database"
        pg_dump "$conn" --role=healthyhires_app --format=custom --no-owner --file="$dir/healthyhires.dump"
        log "Downloading blobs from $account"
        for container in $(az storage container list --account-name "$account" --auth-mode login --query '[].name' -o tsv); do
            az storage blob download-batch --account-name "$account" --auth-mode login \
                --source "$container" --destination "$dir/blobs/$container" --output none
            log "  $container"
        done
        log "Export complete in $dir"
        ;;
    import)
        [[ -f $dir/healthyhires.dump ]] || die "$dir/healthyhires.dump not found"
        log "Restoring the database"
        # The schemas already exist (db-grant-identity.sh creates them, and the application role may not), so
        # their CREATE SCHEMA entries are left out of the restore.
        pg_restore --list "$dir/healthyhires.dump" | grep -v ' SCHEMA - ' > "$dir/restore.list"
        pg_restore --dbname="$conn" --role=healthyhires_app --no-owner --exit-on-error \
            --use-list="$dir/restore.list" "$dir/healthyhires.dump"
        log "Uploading blobs to $account"
        for path in "$dir"/blobs/*/; do
            [[ -d $path ]] || continue
            container=$(basename "$path")
            az storage container create --account-name "$account" --auth-mode login --name "$container" --output none
            az storage blob upload-batch --account-name "$account" --auth-mode login \
                --destination "$container" --source "$path" --output none
            log "  $container"
        done
        log "Import complete"
        ;;
esac
