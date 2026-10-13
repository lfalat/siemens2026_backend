#!/usr/bin/env bash
# Creates or updates the HealthyHires Azure resources from infra/main.bicep (ADR-023), and copies the values the
# deploy workflow needs into the GitHub "production" environment. Works on any subscription: everything specific
# to a person or subscription comes from infra/azure.env. Walkthrough: docs/operations/azure-deployment-guide.md
#
#   scripts/azure/provision.sh check     preflight: providers, region, VM size, quota, image
#   scripts/azure/provision.sh what-if   show what a deploy would change
#   scripts/azure/provision.sh deploy    create or update the resources
#   scripts/azure/provision.sh github    set the GitHub environment variables and secrets from the outputs
#   scripts/azure/provision.sh breakglass  set a new Serial Console password; its only copy goes to Key Vault
set -euo pipefail
source "$(dirname "$0")/lib.sh"

providers=(Microsoft.Compute Microsoft.Network Microsoft.Storage Microsoft.KeyVault Microsoft.DBforPostgreSQL
    Microsoft.OperationalInsights Microsoft.Insights Microsoft.AlertsManagement Microsoft.Communication
    Microsoft.Consumption Microsoft.PolicyInsights Microsoft.CognitiveServices)

check() {
    log "Subscription"
    az account show --query '{name: name, id: id, user: user.name, tenant: tenantId}' -o table

    log "Registering resource providers (a new subscription has most of them off)"
    local ns
    for ns in "${providers[@]}"; do
        az provider register --namespace "$ns" --wait > /dev/null
    done

    log "Region $HH_LOCATION"
    [[ $(az account list-locations --query "[?name=='$HH_LOCATION'] | length(@)" -o tsv) == 1 ]] \
        || die "unknown region $HH_LOCATION"

    local size=${HH_VM_SIZE:-Standard_B2s}
    log "VM size $size"
    local restrictions
    restrictions=$(az vm list-skus --location "$HH_LOCATION" --size "$size" --resource-type virtualMachines \
        --query "[?name=='$size'].restrictions[].reasonCode" -o tsv)
    [[ -z $restrictions ]] || die "$size is restricted in $HH_LOCATION for this subscription ($restrictions); set HH_VM_SIZE or HH_LOCATION"

    log "vCPU quota in $HH_LOCATION"
    az vm list-usage --location "$HH_LOCATION" \
        --query "[?name.value=='cores' || name.value=='standardBSFamily'].{quota: localName, used: currentValue, limit: limit}" -o table

    image_check

    if [[ ! -f ${deploy_key:-}.pub ]]; then
        log "Generating the CI deploy key in $key_dir"
        install -d -m 700 "$key_dir"
        ssh-keygen -q -t ed25519 -N '' -C "healthyhires-deploy@$HH_SUBSCRIPTION_ID" -f "$deploy_key"
    fi

    log "Preflight passed. Next: scripts/azure/provision.sh what-if"
}

# Resolves the AlmaLinux image and, if the Marketplace offer has a purchase plan, accepts its terms and passes
# the plan to Bicep.
image_check() {
    local offer=${HH_IMAGE_OFFER:-almalinux-x86_64} sku=${HH_IMAGE_SKU:-10-gen2} urn plan
    log "Image almalinux:$offer:$sku"
    urn=$(az vm image list --location "$HH_LOCATION" --publisher almalinux --offer "$offer" --sku "$sku" --all \
        --query '[-1].urn' -o tsv)
    [[ -n $urn ]] || die "no image almalinux:$offer:$sku in $HH_LOCATION; list them with: az vm image list -p almalinux --all -l $HH_LOCATION -o table"
    plan=$(az vm image show --location "$HH_LOCATION" --urn "$urn" --query plan -o json)
    if [[ $plan != null && -n $plan ]]; then
        az vm image terms accept --urn "$urn" > /dev/null
        HH_IMAGE_PLAN_JSON=$(jq -c '{name: .name, product: .product, publisher: .publisher}' <<< "$plan")
    else
        HH_IMAGE_PLAN_JSON='{}'
    fi
    export HH_IMAGE_PLAN_JSON
}

require_deploy_key() {
    [[ -n ${HH_DEPLOY_SSH_PUBLIC_KEY:-} ]] || die "no deploy key for this subscription yet; run: scripts/azure/provision.sh check"
}

what_if() {
    require_deploy_key
    image_check
    az deployment sub what-if --name "$deployment" --location "$HH_LOCATION" \
        --template-file "$root/infra/main.bicep" --parameters "$root/infra/main.bicepparam"
}

deploy() {
    require_deploy_key
    image_check
    log "Deploying $deployment to $HH_LOCATION (about 10–15 minutes; PostgreSQL is the slow part)"
    az deployment sub create --name "$deployment" --location "$HH_LOCATION" \
        --template-file "$root/infra/main.bicep" --parameters "$root/infra/main.bicepparam" \
        --query 'properties.outputs.{app: appHost.value, vm: vmName.value, postgres: postgresHost.value, keyVault: keyVaultUri.value}' -o table
    budget
    log "Done. Next: scripts/azure/enrol-netbird.sh"
}

# The budget is its own deployment, so an offer without Cost Management budgets does not fail the rest.
budget() {
    local amount=${HH_BUDGET_AMOUNT:-0}
    (( amount > 0 )) || { warn "HH_BUDGET_AMOUNT is 0: no budget; watch Cost analysis by hand"; return 0; }
    log "Budget of $amount per month"
    if ! az deployment sub create --name "$deployment-budget" --location "$HH_LOCATION" \
        --template-file "$root/infra/budget.bicep" \
        --parameters amount="$amount" alertEmail="$HH_ALERT_EMAIL" environmentName="$HH_ENVIRONMENT" \
        --output none; then
        warn "Azure refused the budget (some student offers do not support it); watch Cost analysis by hand (guide §1.3)"
    fi
}

github() {
    need gh jq
    require_deploy_key
    local repo=$HH_GITHUB_REPOSITORY env=production principal netbird_ip host_key
    for name in HH_ENTRA_TENANT_ID HH_ENTRA_CLIENT_ID HH_API_SCOPE; do
        [[ -n "${!name:-}" ]] || die "$name is empty in $env_file (guide §4)"
    done

    log "Reading the VM's NetBird address and SSH host key through Run Command"
    netbird_ip=$(run_on_vm 'netbird status --json | jq -r .netbirdIp' | cut -d/ -f1 | tr -d '[:space:]')
    [[ $netbird_ip =~ ^[0-9.]+$ ]] || die "the VM is not on NetBird yet; run scripts/azure/enrol-netbird.sh first"
    host_key=$(run_on_vm 'cat /etc/ssh/ssh_host_ed25519_key.pub' | awk '/^ssh-ed25519/ { print $1, $2; exit }')
    [[ -n $host_key ]] || die "could not read the VM's SSH host key"

    principal=$(output vmPrincipalId)

    log "Setting variables and secrets of the GitHub environment $env in $repo"
    gh api --method PUT "repos/$repo/environments/$env" > /dev/null
    set_var() { gh variable set "$1" --repo "$repo" --env "$env" --body "$2"; }
    set_var APP_HOST "$(output appHost)"
    set_var ACME_EMAIL "$HH_ALERT_EMAIL"
    set_var BLOB_ENDPOINT "$(output blobEndpoint)"
    set_var PG_HOST "$(output postgresHost)"
    set_var PG_USER "hh-api-${principal:0:8}"
    set_var KEY_VAULT_URI "$(output keyVaultUri)"
    set_var OPENAI_ENDPOINT "$(output openAiEndpoint)"
    set_var OPENAI_DEPLOYMENT "$(output openAiDeployment)"
    set_var ENTRA_TENANT_ID "$HH_ENTRA_TENANT_ID"
    set_var ENTRA_CLIENT_ID "$HH_ENTRA_CLIENT_ID"
    set_var API_SCOPE "$HH_API_SCOPE"
    set_var VM_SSH_HOST "$netbird_ip"
    set_var VM_SSH_HOST_KEY "$host_key"
    gh secret set DEPLOY_SSH_KEY --repo "$repo" --env "$env" < "$deploy_key"

    if ! gh secret list --repo "$repo" --env "$env" --json name -q '.[].name' | grep -qx NETBIRD_SETUP_KEY; then
        warn "NETBIRD_SETUP_KEY is not set yet: gh secret set NETBIRD_SETUP_KEY --repo $repo --env $env (guide §5)"
    fi
    log "Done. Next: scripts/azure/db-grant-identity.sh"
}

# Serial Console needs a local password (iam.md §7). It is generated here, stored only in Key Vault, and only its
# hash reaches the VM, so no plaintext copy exists on the VM, in Run Command logs or on this machine.
breakglass() {
    need openssl
    local vault password hash
    vault=$(output keyVaultName)
    password=$(openssl rand -base64 30 | tr -d '\n')
    hash=$(openssl passwd -6 -stdin <<< "$password")
    log "Storing the break-glass password in Key Vault $vault"
    az keyvault secret set --vault-name "$vault" --name breakglass-password --file <(printf '%s' "$password") --output none
    log "Setting it for the breakglass user on the VM"
    run_on_vm "usermod -p '$hash' breakglass && echo ok" | grep -qx ok || die "could not set the password on the VM"
    unset password
    log "Done. Read it when needed: az keyvault secret show --vault-name $vault --name breakglass-password --query value -o tsv"
}

load_env
case "${1:-}" in
    check) check ;;
    what-if) what_if ;;
    deploy) deploy ;;
    github) github ;;
    breakglass) breakglass ;;
    *) die "usage: provision.sh check|what-if|deploy|github|breakglass" ;;
esac
