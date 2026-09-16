#!/bin/sh
set -eu

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
smoke_project="upaffe-smoke-$$"
smoke_app_port=${UPAFFE_SMOKE_APP_PORT:-18080}
smoke_db_port=${UPAFFE_SMOKE_DB_PORT:-15432}
smoke_http_target=${UPAFFE_SMOKE_HTTP_TARGET:-https://example.com/}
compose_file="$root/deploy/docker-compose.dev.yml"
system_dir=$(mktemp -d "${TMPDIR:-/tmp}/upaffe-system.XXXXXX")
bootstrap_proof=$(
  node -e 'process.stdout.write("test_" + require("node:crypto").randomBytes(32).toString("base64url"))'
)
operator_password=$(
  node -e 'process.stdout.write("test_" + require("node:crypto").randomBytes(24).toString("base64url"))'
)
operator_email="operator@example.test"
cookie_jar="$system_dir/browser.cookies"
ua="$system_dir/ua"

cleanup() {
  UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
    docker compose -p "$smoke_project" -f "$compose_file" down --volumes >/dev/null 2>&1 || true
  rm -rf "$system_dir"
}
trap cleanup EXIT

json_value() {
  node -e '
    let body = "";
    process.stdin.setEncoding("utf8");
    process.stdin.on("data", chunk => body += chunk);
    process.stdin.on("end", () => {
      const value = JSON.parse(body)[process.argv[1]];
      if (value === undefined || value === null) process.exit(2);
      process.stdout.write(String(value));
    });
  ' "$1"
}

assert_absent() {
  secret=$1
  shift
  [ -n "$secret" ] || return 0
  for inspected_file in "$@"; do
    if grep -F "$secret" "$inspected_file" >/dev/null 2>&1; then
      echo "sensitive value appeared in $inspected_file" >&2
      return 1
    fi
  done
}

UPAFFE_BOOTSTRAP_SECRET="$bootstrap_proof" \
UPAFFE_DEV_PORT="$smoke_app_port" \
UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" up --build --wait

base_url="http://localhost:$smoke_app_port"
origin="http://localhost:$smoke_app_port"

# The same host serves the compiled web application and its API.
curl --fail --silent --show-error "$base_url/" >"$system_dir/web.html"
grep '<div id="root"></div>' "$system_dir/web.html" >/dev/null
curl --fail --silent --show-error "$base_url/api/health/live" | grep '"status":"live"' >/dev/null
curl --fail --silent --show-error "$base_url/api/health/ready" | grep '"status":"ready"' >/dev/null

# Begin from the empty database and establish exactly one operator.
curl --fail --silent --show-error "$base_url/api/bootstrap" >"$system_dir/bootstrap-before.json"
grep '"required":true' "$system_dir/bootstrap-before.json" >/dev/null
grep '"available":true' "$system_dir/bootstrap-before.json" >/dev/null
curl --fail --silent --show-error \
  --request POST \
  --header 'Content-Type: application/json' \
  --data-binary @- \
  "$base_url/api/bootstrap" >"$system_dir/bootstrap-response.txt" <<EOF
{"proof":"$bootstrap_proof","email":"$operator_email","password":"$operator_password"}
EOF

second_status=$(curl --silent --show-error \
  --output "$system_dir/bootstrap-second.json" \
  --write-out '%{http_code}' \
  --request POST \
  --header 'Content-Type: application/json' \
  --data-binary @- \
  "$base_url/api/bootstrap" <<EOF
{"proof":"$bootstrap_proof","email":"second@example.test","password":"$operator_password"}
EOF
)
[ "$second_status" = "409" ]
grep '"code":"bootstrap_closed"' "$system_dir/bootstrap-second.json" >/dev/null

# Sign in on the browser path. This cookie is the web application's access path.
curl --fail --silent --show-error \
  --cookie-jar "$cookie_jar" \
  --request POST \
  --header 'Content-Type: application/json' \
  --data-binary @- \
  "$base_url/api/session" >"$system_dir/sign-in-response.txt" <<EOF
{"email":"$operator_email","password":"$operator_password"}
EOF
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  "$base_url/api/session" >"$system_dir/session.json"
grep '"access_path":"browser_session"' "$system_dir/session.json" >/dev/null
session_secret=$(awk 'NF >= 7 { print $7 }' "$cookie_jar" | tail -n 1)

# The browser path creates the first credential explicitly, then creates the project used by all surfaces.
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  --data-binary '{"name":"system agent"}' \
  "$base_url/api/management-credentials" >"$system_dir/credential-issued.json"
credential_id=$(json_value id <"$system_dir/credential-issued.json")
credential_token=$(json_value token <"$system_dir/credential-issued.json")

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  --data-binary '{"key":"system-project","name":"System project"}' \
  "$base_url/api/projects" >"$system_dir/project-browser-create.json"
project_id=$(json_value id <"$system_dir/project-browser-create.json")

# The real generated CLI reads and renames that same project.
go -C "$root/src/cli" generate ./...
go -C "$root/src/cli" build -o "$ua" ./cmd/ua
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project get system-project --json >"$system_dir/project-cli-get.json"
[ "$(json_value id <"$system_dir/project-cli-get.json")" = "$project_id" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project rename system-project --name 'System project renamed' --version 1 --json \
  >"$system_dir/project-cli-rename.json"
[ "$(json_value version <"$system_dir/project-cli-rename.json")" = "2" ]

# A direct bearer API call deletes it; the browser path observes and restores it.
curl --fail --silent --show-error \
  --request DELETE \
  --header "Authorization: Bearer $credential_token" \
  "$base_url/api/projects/system-project?version=2" >"$system_dir/project-api-delete.json"
[ "$(json_value version <"$system_dir/project-api-delete.json")" = "3" ]
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  "$base_url/api/projects?deleted=true" >"$system_dir/project-browser-deleted.json"
grep '"key":"system-project"' "$system_dir/project-browser-deleted.json" >/dev/null
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  --data-binary '{"version":3}' \
  "$base_url/api/projects/system-project/restore" >"$system_dir/project-browser-restore.json"
[ "$(json_value id <"$system_dir/project-browser-restore.json")" = "$project_id" ]
[ "$(json_value version <"$system_dir/project-browser-restore.json")" = "4" ]

# The real CLI creates an HTTP monitor against a public documentation host. A
# fresh monitor is due immediately, so this observes a scheduled success without
# waiting for its five-minute recurring interval.
node -e '
  const target = process.argv[1];
  process.stdout.write(JSON.stringify({
    key: "public-homepage",
    name: "Public homepage",
    target_url: target,
    expected_status_code: 200,
    text_condition: "none",
    text_fragment: null,
    interval_seconds: 300,
    timeout_seconds: 15,
    failure_threshold: 2,
    instruction: "Inspect the public documentation endpoint.",
    runbook_url: "https://docs.example.test/runbooks/public-homepage",
    headers: null,
  }));
' "$smoke_http_target" >"$system_dir/monitor-create-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor create system-project --file "$system_dir/monitor-create-input.json" --json \
  >"$system_dir/monitor-cli-create.json"
[ "$(json_value key <"$system_dir/monitor-cli-create.json")" = "public-homepage" ]

scheduled_ready=false
attempt=0
while [ "$attempt" -lt 60 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" monitor checks system-project public-homepage --json \
    >"$system_dir/monitor-scheduled-checks.json"
  if node -e '
    const page = require(process.argv[1]);
    if (!page.items.some(item => item.trigger === "scheduled" && item.outcome === "success")) process.exit(1);
  ' "$system_dir/monitor-scheduled-checks.json"; then
    scheduled_ready=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$scheduled_ready" = "true" ]

# A prompt CLI test uses the same executor and persists a requested success.
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor test system-project public-homepage --json \
  >"$system_dir/monitor-cli-test-success.json"
[ "$(json_value succeeded <"$system_dir/monitor-cli-test-success.json")" = "true" ]

# Restart while the next scheduled run is pending. PostgreSQL retains both its
# due time and healthy state.
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-before-planning-restart.json"
planned_for=$(json_value next_check_at <"$system_dir/monitor-before-planning-restart.json")
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" up --wait >/dev/null
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-after-planning-restart.json"
[ "$(json_value next_check_at <"$system_dir/monitor-after-planning-restart.json")" = "$planned_for" ]
[ "$(json_value state <"$system_dir/monitor-after-planning-restart.json")" = "healthy" ]

# A deliberately wrong expected status creates two controlled failures. The
# configured threshold is two, so exactly one incident opens on the second.
monitor_version=$(json_value version <"$system_dir/monitor-after-planning-restart.json")
node -e '
  const version = Number(process.argv[1]);
  process.stdout.write(JSON.stringify({
    name: "Public homepage",
    target_url: null,
    expected_status_code: 204,
    text_condition: "none",
    text_fragment: null,
    interval_seconds: 300,
    timeout_seconds: 15,
    failure_threshold: 2,
    instruction: "Inspect the public documentation endpoint.",
    runbook_url: "https://docs.example.test/runbooks/public-homepage",
    version,
  }));
' "$monitor_version" >"$system_dir/monitor-failing-update-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor update system-project public-homepage \
  --file "$system_dir/monitor-failing-update-input.json" --json \
  >"$system_dir/monitor-failing-update.json"

set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor test system-project public-homepage --json \
  >"$system_dir/monitor-first-failure.json" 2>"$system_dir/monitor-first-failure-diagnostic.txt"
first_failure_exit=$?
set -e
[ "$first_failure_exit" = "5" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-below-threshold.json"
node -e '
  const monitor = require(process.argv[1]);
  if (monitor.state !== "failing" || monitor.consecutive_failures !== 1 || monitor.open_incident_id !== null) process.exit(1);
' "$system_dir/monitor-below-threshold.json"

set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor test system-project public-homepage --json \
  >"$system_dir/monitor-second-failure.json" 2>"$system_dir/monitor-second-failure-diagnostic.txt"
second_failure_exit=$?
set -e
[ "$second_failure_exit" = "5" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-open-incident.json"
open_incident_id=$(json_value open_incident_id <"$system_dir/monitor-open-incident.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor incidents system-project public-homepage --json \
  >"$system_dir/monitor-one-incident.json"
node -e '
  const page = require(process.argv[1]);
  if (page.items.length !== 1 || page.items[0].resolved_at !== null) process.exit(1);
' "$system_dir/monitor-one-incident.json"

# Restart with that incident open and prove both current state and incident
# identity survive.
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" up --wait >/dev/null
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-after-incident-restart.json"
[ "$(json_value open_incident_id <"$system_dir/monitor-after-incident-restart.json")" = "$open_incident_id" ]

# Pause through the CLI. While paused, set and remove a write-only header and
# prove its submitted value does not return in ordinary CLI output.
monitor_version=$(json_value version <"$system_dir/monitor-after-incident-restart.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor pause system-project public-homepage --version "$monitor_version" --json \
  >"$system_dir/monitor-cli-pause.json"
[ "$(json_value state <"$system_dir/monitor-cli-pause.json")" = "paused" ]
monitor_header_secret=$(
  node -e 'process.stdout.write("smoke_" + require("node:crypto").randomBytes(24).toString("base64url"))'
)
monitor_version=$(json_value version <"$system_dir/monitor-cli-pause.json")
node -e '
  process.stdout.write(JSON.stringify({ value: process.argv[1], version: Number(process.argv[2]) }));
' "$monitor_header_secret" "$monitor_version" >"$system_dir/monitor-header-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor header set system-project public-homepage X-Smoke-Secret \
  --file "$system_dir/monitor-header-input.json" --json \
  >"$system_dir/monitor-header-set.json"
assert_absent "$monitor_header_secret" "$system_dir/monitor-header-set.json"
monitor_version=$(json_value version <"$system_dir/monitor-header-set.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor header remove system-project public-homepage X-Smoke-Secret \
  --version "$monitor_version" --json >"$system_dir/monitor-header-remove.json"

# Resume through the browser-session path used by the web application. Wait for
# its fresh scheduled failure, then pause through that same path.
monitor_version=$(json_value version <"$system_dir/monitor-header-remove.json")
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  --data-binary "{\"version\":$monitor_version}" \
  "$base_url/api/projects/system-project/http-monitors/public-homepage/resume" \
  >"$system_dir/monitor-browser-resume.json"
[ "$(json_value state <"$system_dir/monitor-browser-resume.json")" = "untested" ]

resumed_scheduled=false
attempt=0
while [ "$attempt" -lt 60 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" monitor checks system-project public-homepage --json \
    >"$system_dir/monitor-resumed-checks.json"
  if node -e '
    const page = require(process.argv[1]);
    if (page.items.filter(item => item.trigger === "scheduled").length < 2) process.exit(1);
  ' "$system_dir/monitor-resumed-checks.json"; then
    resumed_scheduled=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$resumed_scheduled" = "true" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-before-browser-pause.json"
monitor_version=$(json_value version <"$system_dir/monitor-before-browser-pause.json")
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request POST \
  --header 'Content-Type: application/json' \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  --data-binary "{\"version\":$monitor_version}" \
  "$base_url/api/projects/system-project/http-monitors/public-homepage/pause" \
  >"$system_dir/monitor-browser-pause.json"
[ "$(json_value state <"$system_dir/monitor-browser-pause.json")" = "paused" ]

# Restore the good expectation while paused, resume via CLI, and recover the
# existing incident with an immediate browser-session test.
monitor_version=$(json_value version <"$system_dir/monitor-browser-pause.json")
node -e '
  const version = Number(process.argv[1]);
  process.stdout.write(JSON.stringify({
    name: "Public homepage",
    target_url: null,
    expected_status_code: 200,
    text_condition: "none",
    text_fragment: null,
    interval_seconds: 300,
    timeout_seconds: 15,
    failure_threshold: 2,
    instruction: "Inspect the public documentation endpoint.",
    runbook_url: "https://docs.example.test/runbooks/public-homepage",
    version,
  }));
' "$monitor_version" >"$system_dir/monitor-recovery-update-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor update system-project public-homepage \
  --file "$system_dir/monitor-recovery-update-input.json" --json \
  >"$system_dir/monitor-recovery-update.json"
monitor_version=$(json_value version <"$system_dir/monitor-recovery-update.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor resume system-project public-homepage --version "$monitor_version" --json \
  >"$system_dir/monitor-cli-resume.json"
curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request POST \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  "$base_url/api/projects/system-project/http-monitors/public-homepage/test" \
  >"$system_dir/monitor-browser-test-recovery.json"
[ "$(json_value succeeded <"$system_dir/monitor-browser-test-recovery.json")" = "true" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor incidents system-project public-homepage --json \
  >"$system_dir/monitor-resolved-incident.json"
node -e '
  const page = require(process.argv[1]);
  if (page.items.length !== 1 || page.items[0].resolved_at === null) process.exit(1);
' "$system_dir/monitor-resolved-incident.json"

# Rotation admits both tokens during overlap. Revocation rejects both immediately.
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" credential rotate "$credential_id" --json >"$system_dir/credential-rotated.json"
rotated_token=$(json_value token <"$system_dir/credential-rotated.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project get system-project --json >"$system_dir/project-old-token.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$rotated_token" \
  "$ua" project get system-project --json >"$system_dir/project-new-token.json"

curl --fail --silent --show-error \
  --cookie "$cookie_jar" \
  --request DELETE \
  --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $origin" \
  "$base_url/api/management-credentials/$credential_id" >"$system_dir/revoke-response.txt"

set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$rotated_token" \
  "$ua" project get system-project >"$system_dir/revoked-output.txt" 2>"$system_dir/revoked-diagnostic.txt"
revoked_exit=$?
set -e
[ "$revoked_exit" = "7" ]
grep 'authentication_rejected' "$system_dir/revoked-diagnostic.txt" >/dev/null
set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project get system-project >"$system_dir/revoked-old-output.txt" 2>"$system_dir/revoked-old-diagnostic.txt"
revoked_old_exit=$?
set -e
[ "$revoked_old_exit" = "7" ]
grep 'authentication_rejected' "$system_dir/revoked-old-diagnostic.txt" >/dev/null

# Database identity remains singular and the same project row survives every surface.
operator_count=$(UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" exec -T db \
  psql -U upaffe -d upaffe -Atc 'select count(*) from operator_identity')
project_count=$(UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" exec -T db \
  psql -U upaffe -d upaffe -Atc "select count(*) from project where key = 'system-project'")
[ "$operator_count" = "1" ]
[ "$project_count" = "1" ]

UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" logs --no-color app >"$system_dir/app.log"

# Explicit create/rotate files are the only allowed token-revealing artifacts and are excluded here.
ordinary_files="
$system_dir/web.html
$system_dir/bootstrap-before.json
$system_dir/bootstrap-response.txt
$system_dir/bootstrap-second.json
$system_dir/sign-in-response.txt
$system_dir/session.json
$system_dir/project-browser-create.json
$system_dir/project-cli-get.json
$system_dir/project-cli-rename.json
$system_dir/project-api-delete.json
$system_dir/project-browser-deleted.json
$system_dir/project-browser-restore.json
$system_dir/project-old-token.json
$system_dir/project-new-token.json
$system_dir/monitor-cli-create.json
$system_dir/monitor-scheduled-checks.json
$system_dir/monitor-cli-test-success.json
$system_dir/monitor-before-planning-restart.json
$system_dir/monitor-after-planning-restart.json
$system_dir/monitor-failing-update.json
$system_dir/monitor-first-failure.json
$system_dir/monitor-first-failure-diagnostic.txt
$system_dir/monitor-below-threshold.json
$system_dir/monitor-second-failure.json
$system_dir/monitor-second-failure-diagnostic.txt
$system_dir/monitor-open-incident.json
$system_dir/monitor-one-incident.json
$system_dir/monitor-after-incident-restart.json
$system_dir/monitor-cli-pause.json
$system_dir/monitor-header-set.json
$system_dir/monitor-header-remove.json
$system_dir/monitor-browser-resume.json
$system_dir/monitor-resumed-checks.json
$system_dir/monitor-before-browser-pause.json
$system_dir/monitor-browser-pause.json
$system_dir/monitor-recovery-update.json
$system_dir/monitor-cli-resume.json
$system_dir/monitor-browser-test-recovery.json
$system_dir/monitor-resolved-incident.json
$system_dir/revoke-response.txt
$system_dir/revoked-output.txt
$system_dir/revoked-diagnostic.txt
$system_dir/revoked-old-output.txt
$system_dir/revoked-old-diagnostic.txt
$system_dir/app.log
"
assert_absent "$bootstrap_proof" $ordinary_files
assert_absent "$operator_password" $ordinary_files
assert_absent "$session_secret" $ordinary_files
assert_absent "$credential_token" $ordinary_files
assert_absent "$rotated_token" $ordinary_files
assert_absent "$monitor_header_secret" $ordinary_files

echo "access, project, and HTTP monitoring system test passed"
