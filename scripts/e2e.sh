#!/usr/bin/env bash
# The end-to-end tests without remembering any commands (docs/testing/e2e.md). Called by the Justfile:
#
#   just e2e-export   writes the test users from your user secrets to e2e-users.json, to hand to a teammate
#   just e2e-setup    one-time setup: the browser, then the test users from e2e-users.json or typed in
#   just e2e          checks the browser and the running stack, then runs the tests
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
project="$root/Tests/e2e"
handover="$root/e2e-users.json"
roles=(Admin Recruiter HiringManager)
base_url=${E2E__BaseUrl:-http://localhost:5173}

say() { printf '\033[1m%s\033[0m\n' "$*"; }
fail() { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 1; }

need_tools() {
    command -v dotnet > /dev/null || fail "The .NET SDK is not installed. Install .NET 10 from https://dot.net and try again."
    command -v npx > /dev/null || fail "Node.js is not installed. Install it from https://nodejs.org (it is also needed for the frontend) and try again."
}

secrets_json() {
    dotnet user-secrets list --json --project "$project" | sed '/^\/\/BEGIN$/d; /^\/\/END$/d'
}

# The Chromium of the Playwright version the tests use. Quick when it is already there. The Node CLI warns that this
# repository has no npm dependency on Playwright, which is deliberate (the tests are .NET), so its output is shown
# only when the install fails.
install_browser() {
    local version output
    version=$(sed -n 's/.*"Microsoft.Playwright.Xunit.v3" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")
    if ! output=$(npx --yes "playwright@$version" install chromium 2>&1); then
        printf '%s\n' "$output" >&2
        fail "Installing the test browser failed; see the output above."
    fi
}

export_users() {
    need_tools
    local json
    json=$(secrets_json)
    grep -q '"E2E:Users:' <<< "$json" || fail "No test users in your user secrets yet; see docs/testing/e2e.md §1.2."
    (umask 077 && printf '%s\n' "$json" > "$handover")
    say "Wrote $handover."
    echo "It contains the test users' passwords and authenticator secrets. Send it privately (not by e-mail or chat"
    echo "history you keep), have it placed in the repository root, and run 'just e2e-setup' there. Delete your copy."
}

setup() {
    need_tools
    say "1/2  Installing the test browser (a minute the first time)..."
    install_browser

    say "2/2  Test users"
    if [[ -f $handover ]]; then
        dotnet user-secrets set --project "$project" < "$handover" > /dev/null
        rm -f "$handover"
        echo "Imported them from e2e-users.json, and deleted the file."
    else
        echo "No e2e-users.json here, so type them in. Leave the username empty to skip a role."
        local role username password secret
        for role in "${roles[@]}"; do
            read -rp "  $role username: " username
            [[ -n $username ]] || continue
            read -rsp "  $role password: " password && echo
            read -rsp "  $role authenticator secret key: " secret && echo
            dotnet user-secrets set "E2E:Users:$role:Username" "$username" --project "$project" > /dev/null
            dotnet user-secrets set "E2E:Users:$role:Password" "$password" --project "$project" > /dev/null
            dotnet user-secrets set "E2E:Users:$role:TotpSecret" "${secret// /}" --project "$project" > /dev/null
        done
    fi
    say "Done. Start the app ('just aspire-up' and 'just app', each in its own terminal), then run 'just e2e'."
}

run() {
    need_tools
    install_browser

    # Through the frontend's /api proxy, so one request checks both the frontend and the API behind it.
    if ! curl -fsS -o /dev/null --max-time 5 "$base_url/"; then
        fail "The frontend is not running at $base_url. Start it with 'just app' in its own terminal, then try again."
    fi
    if ! curl -fsS -o /dev/null --max-time 5 "$base_url/api/v1/hello"; then
        fail "The API is not answering behind $base_url. Start it with 'just aspire-up' in its own terminal, wait until the dashboard shows apiservice as Running, then try again."
    fi

    dotnet test --project "$project" "$@"
}

case "${1:-}" in
    export) export_users ;;
    setup) setup ;;
    run) shift; run "$@" ;;
    *) fail "usage: scripts/e2e.sh export|setup|run [dotnet test arguments]" ;;
esac
