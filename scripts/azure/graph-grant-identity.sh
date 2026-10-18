#!/usr/bin/env bash
# Gives the VM's managed identity the Microsoft Graph application permissions the API needs to create staff accounts
# (iam.md §4.2): create the user, find the HealthyHires API's enterprise application, and assign the user an app role
# on it. Run after the first provisioning and after every VM rebuild, because a rebuilt VM gets a new identity
# (infrastructure-operations-architecture.md §4.3).
#
# Granting an application permission is admin consent, so the operator needs the Entra role Privileged Role
# Administrator or Global Administrator. Idempotent: a permission the identity already holds is left alone.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env
need jq

graph_api=https://graph.microsoft.com/v1.0
principal=$(output vmPrincipalId)

# Microsoft Graph's own service principal in this tenant, which defines the permissions as app roles.
graph=$(az rest --method GET \
    --url "$graph_api/servicePrincipals(appId='00000003-0000-0000-c000-000000000000')?\$select=id,appRoles")
graph_id=$(jq -r .id <<< "$graph")
held=$(az rest --method GET --url "$graph_api/servicePrincipals/$principal/appRoleAssignments" \
    --query "value[?resourceId=='$graph_id'].appRoleId" -o tsv)

for permission in User.ReadWrite.All Application.Read.All AppRoleAssignment.ReadWrite.All; do
    role_id=$(jq -r --arg value "$permission" '.appRoles[] | select(.value == $value) | .id' <<< "$graph")
    [[ -n $role_id ]] || die "Microsoft Graph has no application permission $permission"
    if grep -qx "$role_id" <<< "$held"; then
        log "already granted: $permission"
        continue
    fi
    az rest --method POST --url "$graph_api/servicePrincipals/$principal/appRoleAssignments" \
        --body "{\"principalId\":\"$principal\",\"resourceId\":\"$graph_id\",\"appRoleId\":\"$role_id\"}" --output none
    log "granted: $permission"
done

log "Done: the API can create staff accounts. New permissions reach the identity's tokens within about an hour."
