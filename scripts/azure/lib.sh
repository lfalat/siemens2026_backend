#!/usr/bin/env bash
# Shared helpers for scripts/azure/*.sh. Sourced, not run.

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
env_file="${HH_ENV_FILE:-$root/infra/azure.env}"

log() { printf '\033[1m==> %s\033[0m\n' "$*" >&2; }
warn() { printf '\033[33mwarning: %s\033[0m\n' "$*" >&2; }
die() { printf '\033[31merror: %s\033[0m\n' "$*" >&2; exit 1; }

need() {
    local tool
    for tool in "$@"; do
        command -v "$tool" > /dev/null || die "$tool is not installed (guide §1)"
    done
}

# Loads infra/azure.env, checks the required values and selects the subscription it names.
load_env() {
    [[ -f $env_file ]] || die "$env_file does not exist; copy infra/azure.env.example and fill it in"
    set -a
    # shellcheck source=/dev/null
    source "$env_file"
    set +a

    local name
    for name in HH_SUBSCRIPTION_ID HH_OPERATOR_OBJECT_ID HH_OPERATOR_UPN HH_GITHUB_REPOSITORY HH_ALERT_EMAIL; do
        [[ -n "${!name:-}" ]] || die "$name is empty in $env_file"
    done

    HH_LOCATION=${HH_LOCATION:-germanywestcentral}
    HH_ENVIRONMENT=${HH_ENVIRONMENT:-prod}
    export HH_LOCATION HH_ENVIRONMENT

    local key_file=${HH_OPERATOR_SSH_PUBLIC_KEY_FILE:-$HOME/.ssh/id_ed25519.pub}
    key_file=${key_file/#\~/$HOME}
    [[ -f $key_file ]] || die "operator SSH public key $key_file not found"
    HH_OPERATOR_SSH_PUBLIC_KEY=$(< "$key_file")
    export HH_OPERATOR_SSH_PUBLIC_KEY

    # The CI deploy key belongs to one subscription's VM; a new subscription gets a new key.
    key_dir="$HOME/.config/healthyhires/$HH_SUBSCRIPTION_ID"
    deploy_key="$key_dir/deploy_ed25519"
    if [[ -f $deploy_key.pub ]]; then
        HH_DEPLOY_SSH_PUBLIC_KEY=$(< "$deploy_key.pub")
        export HH_DEPLOY_SSH_PUBLIC_KEY
    fi

    rg="rg-healthyhires-$HH_ENVIRONMENT"
    deployment="healthyhires-$HH_ENVIRONMENT"

    need az
    az account show > /dev/null 2>&1 || die "not signed in to Azure; run: az login"
    az account set --subscription "$HH_SUBSCRIPTION_ID" || die "cannot select subscription $HH_SUBSCRIPTION_ID"
}

# One output of the last deployment, e.g. output vmName.
output() {
    az deployment sub show --name "$deployment" --query "properties.outputs.$1.value" -o tsv
}

# Runs a shell command on the VM through Azure Run Command (no network path needed) and prints its stdout.
run_on_vm() {
    az vm run-command invoke --resource-group "$rg" --name "$(output vmName)" \
        --command-id RunShellScript --scripts "$1" --query 'value[0].message' -o tsv \
        | awk '/^\[stdout\]/ { out = 1; next } /^\[stderr\]/ { out = 0 } out'
}

# Opens an SSH tunnel to Azure PostgreSQL through the VM over NetBird (the server has no public access) and signs psql
# in with the operator's Entra token. Sets $port, the local end of the tunnel, and closes the tunnel when the script
# exits. Needs this machine on NetBird.
open_pg_tunnel() {
    local pg_host vm_host
    pg_host=$(output postgresHost)
    vm_host=${HH_VM_SSH_HOST:-$(run_on_vm 'netbird status --json | jq -r .netbirdIp' | cut -d/ -f1 | tr -d '[:space:]')}
    port=${HH_TUNNEL_PORT:-6432}

    log "Opening a tunnel to $pg_host through azureops@$vm_host"
    ssh -f -N -o ExitOnForwardFailure=yes -L "$port:$pg_host:5432" "azureops@$vm_host"
    # shellcheck disable=SC2064  # expanded now on purpose: the trap must name this tunnel
    trap "pkill -f 'ssh -f -N -o ExitOnForwardFailure=yes -L $port:$pg_host:5432' || true" EXIT
    sleep 1

    PGPASSWORD=$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)
    export PGPASSWORD PGSSLMODE=require
}
