#!/usr/bin/env bash
# Joins the VM to the NetBird admin network (ADR-014, infrastructure-operations-architecture.md §4.1 step 3).
# The setup key is typed in, sent once through Azure Run Command, and never stored in Bicep, cloud-init or a file.
# Use a one-off key with a short expiry and the auto-assigned group for servers (guide §5).
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env

vm=$(output vmName)
read -r -s -p "One-off NetBird setup key for $vm: " key
echo
[[ $key =~ ^[A-Za-z0-9-]+$ ]] || die "that does not look like a NetBird setup key"

log "Enrolling $vm"
run_on_vm "netbird up --setup-key '$key' --hostname '$vm' > /dev/null && netbird status | grep -E 'Management|NetBird IP|FQDN'"
log "Done. Next: scripts/azure/provision.sh github"
