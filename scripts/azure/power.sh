#!/usr/bin/env bash
# Pauses the deployment between demos to save credit, and resumes it (docs/operations/azure-deployment-guide.md §10.3).
# Pausing deallocates the VM and stops PostgreSQL; everything else, including the data, the public IP and with it the
# hostname, stays. The availability test and the alert rules are switched off while paused, because the VM being
# down is exactly what they report, and switched on again once /health answers.
#
#   scripts/azure/power.sh pause    turn the alerts off, deallocate the VM, stop PostgreSQL
#   scripts/azure/power.sh resume   start PostgreSQL and the VM, wait for /health, turn the alerts back on
#   scripts/azure/power.sh status   show what is running, and warn if PostgreSQL has started by itself
#
# Azure starts a stopped PostgreSQL server again after 7 days. Running pause again stops it; every step is idempotent.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

vm_state() {
    az vm show --resource-group "$rg" --name "$vm" --show-details --query powerState -o tsv
}

pg_state() {
    az postgres flexible-server show --resource-group "$rg" --name "$pg" --query state -o tsv
}

# Turns the availability test, the metric alerts and the log alerts on or off. The activity log alert (role, NSG and
# lock changes) and the budget stay on: pausing does not trigger them.
set_monitoring() {
    local enabled=$1 id
    for id in $(az resource list --resource-group "$rg" --resource-type Microsoft.Insights/webtests --query '[].id' -o tsv); do
        az resource update --ids "$id" --set "properties.Enabled=$enabled" --output none
    done
    for id in $(az monitor metrics alert list --resource-group "$rg" --query '[].id' -o tsv); do
        az monitor metrics alert update --ids "$id" --enabled "$enabled" --output none
    done
    local disabled=true
    [[ $enabled == true ]] && disabled=false
    for id in $(az monitor scheduled-query list --resource-group "$rg" --query '[].id' -o tsv); do
        az monitor scheduled-query update --ids "$id" --disabled "$disabled" --output none
    done
}

pause() {
    log "Turning off the availability test and the alert rules"
    set_monitoring false

    log "Deallocating VM $vm"
    az vm deallocate --resource-group "$rg" --name "$vm" --output none

    case $(pg_state) in
        Stopped) log "PostgreSQL $pg is already stopped" ;;
        Ready)
            log "Stopping PostgreSQL $pg (a few minutes)"
            az postgres flexible-server stop --resource-group "$rg" --name "$pg" --output none
            ;;
        *) die "PostgreSQL $pg is $(pg_state); wait until it is Ready and run pause again" ;;
    esac

    local restart
    restart=$(date -d '+7 days' '+%a %d %b' 2> /dev/null || date -v+7d '+%a %d %b')
    log "Paused. Azure starts PostgreSQL again by itself around $restart; run pause again before then"
}

resume() {
    need curl
    case $(pg_state) in
        Ready) log "PostgreSQL $pg is already running" ;;
        Stopped)
            log "Starting PostgreSQL $pg (a few minutes)"
            az postgres flexible-server start --resource-group "$rg" --name "$pg" --output none
            ;;
        *) die "PostgreSQL $pg is $(pg_state); wait until it is Ready or Stopped and run resume again" ;;
    esac

    # After PostgreSQL, so the API does not start against a database that is not there yet.
    log "Starting VM $vm"
    az vm start --resource-group "$rg" --name "$vm" --output none

    log "Waiting for https://$host/health (up to 10 minutes)"
    for _ in $(seq 60); do
        if curl -fsS --max-time 5 "https://$host/health" > /dev/null 2>&1; then
            log "Healthy. Turning the availability test and the alert rules back on"
            set_monitoring true
            log "Resumed: https://$host"
            return 0
        fi
        sleep 10
    done
    die "/health still fails after 10 minutes; alerts stay off. See guide §12, then run resume again"
}

status() {
    local vm_now pg_now test_on
    vm_now=$(vm_state)
    pg_now=$(pg_state)
    test_on=$(az resource list --resource-group "$rg" --resource-type Microsoft.Insights/webtests \
        --query '[0].id' -o tsv | xargs -r -I{} az resource show --ids {} --query properties.Enabled -o tsv)
    printf '%-40s %s\n' "VM $vm" "$vm_now" "PostgreSQL $pg" "$pg_now" "Availability test" "${test_on:-none}"
    if [[ $vm_now != "VM running" && $pg_now == Ready ]]; then
        warn "PostgreSQL is running while the VM is not: Azure started it after 7 days. Run: just azure-pause"
    fi
}

load_env
vm=$(output vmName)
pg=$(output postgresServerName)
host=$(output appHost)

case "${1:-}" in
    pause) pause ;;
    resume) resume ;;
    status) status ;;
    *) die "usage: power.sh pause|resume|status" ;;
esac
