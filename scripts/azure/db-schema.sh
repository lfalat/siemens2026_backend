#!/usr/bin/env bash
# Creates the application's tables in Azure PostgreSQL from configs/postgres/init/01-create-db.sql, the same script
# the local AppHost loads (ADR-D002, ADR-D005). Run once per new database, after db-grant-identity.sh: that script
# creates the schemas, which the application role may not do itself.
#
# Only 01-create-db.sql: the development seed (02-seed-data.sql) never reaches Azure.
#
# A baseline, not a migration. The DDL is not re-runnable, so this refuses to touch a database that already has tables
# in the application's schemas: a deployed database, or one restored by migrate.sh import. Later schema changes need
# migrations of their own (ADR-D005).
#
# Runs as the operator acting as healthyhires_app, so the tables, functions and triggers belong to the application
# role like the schemas do, and in one transaction, so a failure leaves nothing half-created.
#
# Needs psql 18+ locally and this machine on NetBird, like db-grant-identity.sh.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env
need psql ssh

ddl="$root/configs/postgres/init/01-create-db.sql"
[[ -f $ddl ]] || die "$ddl not found"

# The schemas the DDL creates must be the ones db-grant-identity.sh created. The application role may not create a
# schema, so a schema missing from infra/db-schemas.txt would only fail halfway through the transaction.
mapfile -t ddl_schemas < <(sed -nE 's/^CREATE SCHEMA IF NOT EXISTS ([a-z][a-z0-9_]*);$/\1/p' "$ddl")
mapfile -t granted < <(grep -E '^[a-z][a-z0-9_]*$' "$root/infra/db-schemas.txt")
(( ${#ddl_schemas[@]} > 0 )) || die "found no CREATE SCHEMA statement in $ddl"
for schema in "${ddl_schemas[@]}"; do
    [[ " ${granted[*]} " == *" $schema "* ]] \
        || die "$ddl creates schema $schema, which infra/db-schemas.txt does not list; add it and run just azure-db-grant"
done
schema_list=$(printf "'%s'," "${ddl_schemas[@]}")
schema_list=${schema_list%,}

open_pg_tunnel

app() {
    psql "host=localhost port=$port user=$HH_OPERATOR_UPN dbname=healthyhires options='-c role=healthyhires_app'" \
        -v ON_ERROR_STOP=1 -q "$@"
}

missing=$(app -At -c "SELECT string_agg(s, ', ') FROM unnest(ARRAY[$schema_list]) AS s WHERE to_regnamespace(s) IS NULL")
[[ -z $missing ]] || die "schemas $missing do not exist yet; run just azure-db-grant first"

existing=$(app -At -c "SELECT count(*) FROM pg_tables WHERE schemaname IN ($schema_list)")
if (( existing > 0 )); then
    die "the database already has $existing table(s) in $schema_list; this baseline only runs against an empty one"
fi

log "Creating the tables from configs/postgres/init/01-create-db.sql"
# The CREATE SCHEMA lines are left out: the schemas exist (checked above), and even IF NOT EXISTS needs the CREATE
# privilege on the database, which the application role does not have.
sed -E '/^CREATE SCHEMA IF NOT EXISTS [a-z][a-z0-9_]*;$/d' "$ddl" | app --single-transaction -f -

app -At -c "SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname IN ($schema_list) ORDER BY 1" \
    | while read -r table; do log "  $table"; done
log "Done. Deploy the API now: gh workflow run deploy --repo $HH_GITHUB_REPOSITORY"
