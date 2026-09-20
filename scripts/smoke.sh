#!/bin/sh
# shellcheck disable=SC2086
set -eu
umask 077

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
smoke_project="upaffe-smoke-$$"
smoke_phase=setup
smoke_app_port=${UPAFFE_SMOKE_APP_PORT:-18080}
smoke_db_port=${UPAFFE_SMOKE_DB_PORT:-15432}
smoke_smtp_port=${UPAFFE_SMOKE_SMTP_PORT:-18025}
smoke_http_target=${UPAFFE_SMOKE_HTTP_TARGET:-https://example.com/}
compose_file="$root/deploy/docker-compose.dev.yml"
smoke_compose_file="$root/deploy/docker-compose.smoke.yml"
system_dir=$(mktemp -d "${TMPDIR:-/tmp}/upaffe-system.XXXXXX")
operator_password=$(
  node -e 'process.stdout.write("test_" + require("node:crypto").randomBytes(24).toString("base64url"))'
)
operator_email="operator@example.test"
cookie_jar="$system_dir/browser.cookies"
ua="$system_dir/ua"

cleanup() {
  result=$?
  if [ "$result" -ne 0 ]; then
    printf 'Smoke failed during %s (exit %s).\n' "$smoke_phase" "$result" >&2
    UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
      docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" \
      logs --no-color --tail=200 app 2>/dev/null | \
      sed -nE 's/.*(System\.[A-Za-z0-9_.]*Exception|Microsoft\.EntityFrameworkCore\.[A-Za-z0-9_.]*Exception|Npgsql\.[A-Za-z0-9_.]*Exception):.*/\1/p; s/.*SqlState: ([0-9A-Z]+).*/SqlState \1/p; s/.*at (Upaffe\.[A-Za-z0-9_.]+).*/\1/p' | \
      sort -u >&2 || true
  fi
  UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
    docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" down --volumes >/dev/null 2>&1 || true
  rm -rf "$system_dir"
  trap - EXIT
  exit "$result"
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

UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" build app
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up -d --wait db
printf '%s\n' "$operator_password" > "$system_dir/operator-password"
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" \
  run --rm --no-deps -T app bootstrap --email "$operator_email" \
  --credential-name smoke-agent --password-stdin \
  < "$system_dir/operator-password" > "$system_dir/initial-credential.json"
[ "$(json_value status < "$system_dir/initial-credential.json")" = created ]
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up -d --wait

base_url="http://localhost:$smoke_app_port"
origin="http://localhost:$smoke_app_port"
smtp_fixture="http://localhost:$smoke_smtp_port"

# The same host serves the compiled web application and its API.
curl --fail --silent --show-error "$base_url/" >"$system_dir/web.html"
grep '<div id="root"></div>' "$system_dir/web.html" >/dev/null
curl --fail --silent --show-error "$base_url/api/health/live" | grep '"status":"live"' >/dev/null
curl --fail --silent --show-error "$base_url/api/health/ready" | grep '"status":"ready"' >/dev/null

# A repeated command cannot reveal another credential or change the operator.
if UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" \
  run --rm --no-deps -T app bootstrap --email "$operator_email" \
  --credential-name smoke-agent --password-stdin \
  < "$system_dir/operator-password" > "$system_dir/bootstrap-second.json"; then
  echo 'Repeated bootstrap unexpectedly succeeded.' >&2; exit 1
else
  [ "$?" = 3 ]
fi
[ "$(json_value status < "$system_dir/bootstrap-second.json")" = already_initialized ]
curl --fail --silent --show-error "$base_url/api/bootstrap" >"$system_dir/bootstrap-state.json"
grep '"required":false' "$system_dir/bootstrap-state.json" >/dev/null

# The initial token can administer through the real CLI before any browser sign-in.
initial_token=$(json_value token < "$system_dir/initial-credential.json")
go -C "$root/src/cli" generate ./...
go -C "$root/src/cli" build -o "$ua" ./cmd/ua
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$initial_token" \
  "$ua" project create --key bootstrap-project --name 'Bootstrap project' --json \
  > "$system_dir/bootstrap-project.json"
[ "$(json_value key < "$system_dir/bootstrap-project.json")" = bootstrap-project ]

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

# The browser path creates another credential, then creates the project used by all surfaces.
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
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project get system-project --json >"$system_dir/project-cli-get.json"
[ "$(json_value id <"$system_dir/project-cli-get.json")" = "$project_id" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project create --key cli-project --name 'CLI project' --json \
  >"$system_dir/project-cli-create.json"
cli_project_id=$(json_value id <"$system_dir/project-cli-create.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project create --key cli-project --name 'CLI project' --json \
  >"$system_dir/project-cli-create-repeat.json"
[ "$(json_value id <"$system_dir/project-cli-create-repeat.json")" = "$cli_project_id" ]
[ "$(json_value version <"$system_dir/project-cli-create-repeat.json")" = "1" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project rename system-project --name 'System project renamed' --version 1 --json \
  >"$system_dir/project-cli-rename.json"
[ "$(json_value version <"$system_dir/project-cli-rename.json")" = "2" ]
set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project rename system-project --name 'Stale write' --version 1 --json \
  >"$system_dir/project-stale-output.json" 2>"$system_dir/project-stale-error.json"
stale_exit=$?
set -e
[ "$stale_exit" = "4" ]
[ ! -s "$system_dir/project-stale-output.json" ]
node -e 'const error = require(process.argv[1]); if (error.code !== "conflict" || error.exit_code !== 4 || error.http_status !== 409) process.exit(1)' \
  "$system_dir/project-stale-error.json"

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

# Configure the isolated SMTP receiver through the generated CLI. The password
# is a write-only storage check; this receiver does not advertise authentication.
smtp_password=$(node -e 'process.stdout.write("smtp_" + require("node:crypto").randomBytes(24).toString("base64url"))')
node -e '
  process.stdout.write(JSON.stringify({ version: 0, host: "smtp-fixture", port: 2525,
    security: "none", sender_address: "notify@example.test", sender_name: "upaffe",
    public_base_url: process.argv[1], username: null }));
' "$base_url" >"$system_dir/email-settings-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email settings set --file "$system_dir/email-settings-input.json" --json \
  >"$system_dir/email-settings.json"
node -e 'process.stdout.write(JSON.stringify({ version: 1, password: process.argv[1] }))' \
  "$smtp_password" >"$system_dir/email-password-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email password set --file "$system_dir/email-password-input.json" --json \
  >"$system_dir/email-password-result.json"
assert_absent "$smtp_password" "$system_dir/email-password-result.json"
echo '{"version":2,"recipients":["ops@example.test"]}' >"$system_dir/email-defaults-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email defaults set --file "$system_dir/email-defaults-input.json" --json \
  >"$system_dir/email-defaults.json"
echo '{"version":4,"recipients":["ops@example.test"]}' >"$system_dir/email-project-recipients-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email recipients set system-project --file - --json \
  <"$system_dir/email-project-recipients-input.json" \
  >"$system_dir/email-project-recipients.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email test --recipient ops@example.test --json >"$system_dir/email-test-result.json"
[ "$(json_value status <"$system_dir/email-test-result.json")" = "accepted_by_smtp" ]
curl --fail --silent --show-error "$smtp_fixture/messages" >"$system_dir/smtp-after-test.json"
node -e '
  const result = require(process.argv[1]);
  if (result.messages.length !== 1 || !result.messages[0].content.includes("upaffe SMTP test")) process.exit(1);
' "$system_dir/smtp-after-test.json"

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
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor create system-project --file "$system_dir/monitor-create-input.json" --json \
  >"$system_dir/monitor-cli-create-repeat.json"
[ "$(json_value id <"$system_dir/monitor-cli-create-repeat.json")" = "$(json_value id <"$system_dir/monitor-cli-create.json")" ]

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
smoke_phase='HTTP monitor planned restart'
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor get system-project public-homepage --json \
  >"$system_dir/monitor-before-planning-restart.json"
planned_for=$(json_value next_check_at <"$system_dir/monitor-before-planning-restart.json")
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up --wait >/dev/null
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
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project report system-project --json >"$system_dir/project-report-below-threshold.json"
node -e '
  const report = require(process.argv[1]);
  const monitor = report.attention.find(item => item.type === "http" && item.key === "public-homepage");
  if (!monitor || monitor.state !== "failing" || monitor.failure_count !== 1 ||
      (monitor.incident !== undefined && monitor.incident !== null) ||
      monitor.latest_result?.reason !== "unexpected_status" || !monitor.last_success ||
      !monitor.next_due_at || monitor.instruction !== "Inspect the public documentation endpoint.") process.exit(1);
' "$system_dir/project-report-below-threshold.json"

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

http_alert_accepted=false
attempt=0
while [ "$attempt" -lt 30 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" email incident "$open_incident_id" --monitor-type http --json \
    >"$system_dir/email-http-open.json"
  if node -e '
    const status = require(process.argv[1]);
    if (status.deliveries.filter(item => item.kind === "alert" && item.state === "accepted").length !== 1) process.exit(1);
  ' "$system_dir/email-http-open.json"; then
    http_alert_accepted=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$http_alert_accepted" = "true" ]
curl --fail --silent --show-error "$smtp_fixture/messages" >"$system_dir/smtp-after-http-alert.json"
node -e '
  const result = require(process.argv[1]);
  const alert = result.messages.filter(item => item.content.includes("upaffe alert: system-project/public-homepage"));
  if (alert.length !== 1) process.exit(1);
  const content = alert[0].content;
  for (const fact of ["System project renamed", "Public homepage", "unexpected_status", "Time:", "Details:", process.argv[2], process.argv[3]]) {
    if (!content.includes(fact)) process.exit(1);
  }
' "$system_dir/smtp-after-http-alert.json" "$base_url" "$open_incident_id"

# Restart with that incident open and prove both current state and incident
# identity survive.
smoke_phase='HTTP incident restart'
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up --wait >/dev/null
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
smoke_phase='HTTP monitor browser resume'
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
smoke_phase='HTTP monitor browser pause'
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
smoke_phase='HTTP monitor browser recovery test'
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

http_recovery_accepted=false
attempt=0
while [ "$attempt" -lt 30 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" email incident "$open_incident_id" --monitor-type http --json \
    >"$system_dir/email-http-resolved.json"
  if node -e '
    const status = require(process.argv[1]);
    if (status.deliveries.filter(item => item.kind === "recovery" && item.state === "accepted").length !== 1) process.exit(1);
  ' "$system_dir/email-http-resolved.json"; then
    http_recovery_accepted=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$http_recovery_accepted" = "true" ]
smoke_phase='HTTP recovery SMTP fixture read'
curl --fail --silent --show-error "$smtp_fixture/messages" >"$system_dir/smtp-after-http-recovery.json"
node -e '
  const result = require(process.argv[1]);
  for (const kind of ["alert", "recovery"]) {
    const messages = result.messages.filter(item => item.content.includes(`upaffe ${kind}: system-project/public-homepage`));
    if (messages.length !== 1 || !messages[0].content.includes(process.argv[2])) process.exit(1);
  }
' "$system_dir/smtp-after-http-recovery.json" "$open_incident_id"

# The generated CLI creates both push modes and explicitly issues their
# monitor-scoped reporting credentials. These two files are secret handoff
# artifacts and are deliberately excluded from the ordinary-output scan below.
smoke_phase='push monitoring'
echo "testing composed push monitoring"
node -e '
  process.stdout.write(JSON.stringify({
    key: "nightly-push",
    name: "Nightly push",
    mode: "job_completion",
    interval_seconds: 30,
    tolerance_seconds: 0,
    instruction: "Inspect the fictional backup log.",
    runbook_url: "https://docs.example.test/runbooks/nightly-push",
  }));
' >"$system_dir/push-job-create-input.json"
node -e '
  process.stdout.write(JSON.stringify({
    key: "state-push",
    name: "State push",
    mode: "state_report",
    interval_seconds: 30,
    tolerance_seconds: 0,
    instruction: "Inspect the fictional service state.",
    runbook_url: "https://docs.example.test/runbooks/state-push",
  }));
' >"$system_dir/push-state-create-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push create system-project --file "$system_dir/push-job-create-input.json" --json \
  >"$system_dir/push-job-create.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push create system-project --file "$system_dir/push-state-create-input.json" --json \
  >"$system_dir/push-state-create.json"
[ "$(json_value mode <"$system_dir/push-job-create.json")" = "job_completion" ]
[ "$(json_value mode <"$system_dir/push-state-create.json")" = "state_report" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push create system-project --file - --json \
  <"$system_dir/push-job-create-input.json" >"$system_dir/push-job-create-repeat.json"
[ "$(json_value id <"$system_dir/push-job-create-repeat.json")" = "$(json_value id <"$system_dir/push-job-create.json")" ]

UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential issue system-project nightly-push --json \
  >"$system_dir/push-job-credential-issued.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential issue system-project state-push --json \
  >"$system_dir/push-state-credential-issued.json"
set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential issue system-project nightly-push --json \
  >"$system_dir/push-job-issue-again-output.json" 2>"$system_dir/push-job-issue-again-error.json"
issue_again_exit=$?
set -e
[ "$issue_again_exit" = "4" ]
[ ! -s "$system_dir/push-job-issue-again-output.json" ]
node -e 'const error = require(process.argv[1]); if (error.code !== "conflict" || error.http_status !== 409) process.exit(1)' \
  "$system_dir/push-job-issue-again-error.json"
push_job_token=$(json_value token <"$system_dir/push-job-credential-issued.json")
push_job_url=$(json_value report_url <"$system_dir/push-job-credential-issued.json")
push_job_url_secret=${push_job_url##*/}
push_state_token=$(json_value token <"$system_dir/push-state-credential-issued.json")
push_state_url=$(json_value report_url <"$system_dir/push-state-credential-issued.json")
push_state_url_secret=${push_state_url##*/}

# Malformed variants of a secret report path must also be redacted before
# routing and application diagnostics.
malformed_report_status=$(curl --silent --show-error \
  --output "$system_dir/push-malformed-report.json" --write-out '%{http_code}' \
  "${base_url}${push_job_url}/extra")
[ "$malformed_report_status" = "401" ]

# A reporting credential grants no management read access.
set +e
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$push_job_token" \
  "$ua" push get system-project nightly-push \
  >"$system_dir/push-reporting-token-read.txt" 2>"$system_dir/push-reporting-token-read-diagnostic.txt"
push_read_exit=$?
set -e
[ "$push_read_exit" = "7" ]
grep 'authentication_rejected' "$system_dir/push-reporting-token-read-diagnostic.txt" >/dev/null

# State-report success uses the simple secret URL. Job-completion success uses
# the idempotent JSON route. A job token changes only its bound monitor; the
# state monitor keeps the simple report as its latest observation.
curl --fail --silent --show-error --request POST "${base_url}${push_state_url}" \
  >"$system_dir/push-state-simple-success.txt"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-after-simple.json"
[ "$(json_value state <"$system_dir/push-state-after-simple.json")" = "healthy" ]
push_state_simple_report=$(json_value latest_report_id <"$system_dir/push-state-after-simple.json")

push_job_success_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_job_success_at=$(node -e 'process.stdout.write(new Date(Date.now() - 4000).toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_job_success_id" "$push_job_success_at" >"$system_dir/push-job-success-input.json"
curl --fail --silent --show-error \
  --request POST \
  --header "Authorization: Bearer $push_job_token" \
  --header 'Content-Type: application/json' \
  --data-binary @"$system_dir/push-job-success-input.json" \
  "$base_url/api/reports" >"$system_dir/push-job-success-receipt.json"
[ "$(json_value applied <"$system_dir/push-job-success-receipt.json")" = "true" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-after-job-report.json"
[ "$(json_value latest_report_id <"$system_dir/push-state-after-job-report.json")" = "$push_state_simple_report" ]

# Explicit failures open immediately. An identical retry is a duplicate, a
# second fresh failure updates the same incident, and an older success is kept
# without being allowed to reverse that newer failure.
push_job_failure_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_job_failure_at=$(node -e 'process.stdout.write(new Date(Date.now() - 2000).toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "failure", reason: "fictional backup failed" }));
' "$push_job_failure_id" "$push_job_failure_at" >"$system_dir/push-job-failure-input.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_job_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-job-failure-input.json" \
  "$base_url/api/reports" >"$system_dir/push-job-failure-receipt.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_job_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-job-failure-input.json" \
  "$base_url/api/reports" >"$system_dir/push-job-duplicate-receipt.json"
[ "$(json_value duplicate <"$system_dir/push-job-duplicate-receipt.json")" = "true" ]

push_job_repeat_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_job_repeat_at=$(node -e 'process.stdout.write(new Date(Date.now() - 1000).toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "failure", reason: "fictional backup still failed" }));
' "$push_job_repeat_id" "$push_job_repeat_at" >"$system_dir/push-job-repeat-input.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_job_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-job-repeat-input.json" \
  "$base_url/api/reports" >"$system_dir/push-job-repeat-receipt.json"

push_job_old_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_job_old_at=$(node -e 'process.stdout.write(new Date(Date.now() - 3000).toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_job_old_id" "$push_job_old_at" >"$system_dir/push-job-old-success-input.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_job_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-job-old-success-input.json" \
  "$base_url/api/reports" >"$system_dir/push-job-old-success-receipt.json"
[ "$(json_value applied <"$system_dir/push-job-old-success-receipt.json")" = "false" ]

UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project nightly-push --json >"$system_dir/push-job-open.json"
[ "$(json_value state <"$system_dir/push-job-open.json")" = "failing" ]
push_job_incident=$(json_value open_incident_id <"$system_dir/push-job-open.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push incidents system-project nightly-push --json >"$system_dir/push-job-one-incident.json"
node -e '
  const page = require(process.argv[1]);
  if (page.items.length !== 1 || page.items[0].id !== process.argv[2] || page.items[0].resolved_at !== null) process.exit(1);
' "$system_dir/push-job-one-incident.json" "$push_job_incident"

# The state-report mode follows the same immediate incident rule and retains one
# incident across repeated failure.
push_state_failure_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_state_failure_at=$(node -e 'process.stdout.write(new Date(Date.now() - 1000).toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "failure", reason: "fictional state failed" }));
' "$push_state_failure_id" "$push_state_failure_at" >"$system_dir/push-state-failure-input.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_state_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-state-failure-input.json" \
  "$base_url/api/reports" >"$system_dir/push-state-failure-receipt.json"
push_state_repeat_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_state_repeat_at=$(node -e 'process.stdout.write(new Date().toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "failure", reason: "fictional state still failed" }));
' "$push_state_repeat_id" "$push_state_repeat_at" >"$system_dir/push-state-repeat-input.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_state_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-state-repeat-input.json" \
  "$base_url/api/reports" >"$system_dir/push-state-repeat-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-open.json"
push_state_incident=$(json_value open_incident_id <"$system_dir/push-state-open.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push incidents system-project state-push --json >"$system_dir/push-state-one-incident.json"
node -e '
  const page = require(process.argv[1]);
  if (page.items.length !== 1 || page.items[0].id !== process.argv[2] || page.items[0].resolved_at !== null) process.exit(1);
' "$system_dir/push-state-one-incident.json" "$push_state_incident"
echo "push reports opened one incident per monitor"

push_alerts_accepted=false
attempt=0
while [ "$attempt" -lt 30 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" email incident "$push_job_incident" --monitor-type push --json >"$system_dir/email-push-job-open.json"
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" email incident "$push_state_incident" --monitor-type push --json >"$system_dir/email-push-state-open.json"
  if node -e '
    for (const path of process.argv.slice(1)) {
      const status = require(path);
      if (status.deliveries.filter(item => item.kind === "alert" && item.state === "accepted").length !== 1) process.exit(1);
    }
  ' "$system_dir/email-push-job-open.json" "$system_dir/email-push-state-open.json"; then
    push_alerts_accepted=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$push_alerts_accepted" = "true" ]

# Restart with both incidents open. PostgreSQL preserves the current state,
# incident identity, and each persisted deadline.
push_job_open_deadline=$(json_value next_deadline_at <"$system_dir/push-job-open.json")
push_state_open_deadline=$(json_value next_deadline_at <"$system_dir/push-state-open.json")
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up --wait >/dev/null
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project nightly-push --json >"$system_dir/push-job-after-open-restart.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-after-open-restart.json"
[ "$(json_value open_incident_id <"$system_dir/push-job-after-open-restart.json")" = "$push_job_incident" ]
[ "$(json_value next_deadline_at <"$system_dir/push-job-after-open-restart.json")" = "$push_job_open_deadline" ]
[ "$(json_value open_incident_id <"$system_dir/push-state-after-open-restart.json")" = "$push_state_incident" ]
[ "$(json_value next_deadline_at <"$system_dir/push-state-after-open-restart.json")" = "$push_state_open_deadline" ]
echo "push incidents and deadlines survived restart"

# Fresh successes resolve the retained incidents. The browser-session path then
# reads exactly the same monitor facts that the CLI observes.
push_job_recovery_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_state_recovery_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_recovery_at=$(node -e 'process.stdout.write(new Date().toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_job_recovery_id" "$push_recovery_at" >"$system_dir/push-job-recovery-input.json"
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_state_recovery_id" "$push_recovery_at" >"$system_dir/push-state-recovery-input.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_job_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-job-recovery-input.json" \
  "$base_url/api/reports" >"$system_dir/push-job-recovery-receipt.json"
curl --fail --silent --show-error \
  --request POST --header "Authorization: Bearer $push_state_token" \
  --header 'Content-Type: application/json' --data-binary @"$system_dir/push-state-recovery-input.json" \
  "$base_url/api/reports" >"$system_dir/push-state-recovery-receipt.json"
curl --fail --silent --show-error --cookie "$cookie_jar" \
  "$base_url/api/projects/system-project/push-monitors/nightly-push" \
  >"$system_dir/push-job-browser-recovery.json"
curl --fail --silent --show-error --cookie "$cookie_jar" \
  "$base_url/api/projects/system-project/push-monitors/state-push" \
  >"$system_dir/push-state-browser-recovery.json"
[ "$(json_value state <"$system_dir/push-job-browser-recovery.json")" = "healthy" ]
[ "$(json_value state <"$system_dir/push-state-browser-recovery.json")" = "healthy" ]
echo "fresh push reports recovered both monitors"

push_recoveries_accepted=false
attempt=0
while [ "$attempt" -lt 30 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" email incident "$push_job_incident" --monitor-type push --json >"$system_dir/email-push-job-resolved.json"
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" email incident "$push_state_incident" --monitor-type push --json >"$system_dir/email-push-state-resolved.json"
  if node -e '
    for (const path of process.argv.slice(1)) {
      const status = require(path);
      if (status.deliveries.filter(item => item.kind === "recovery" && item.state === "accepted").length !== 1) process.exit(1);
    }
  ' "$system_dir/email-push-job-resolved.json" "$system_dir/email-push-state-resolved.json"; then
    push_recoveries_accepted=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$push_recoveries_accepted" = "true" ]
curl --fail --silent --show-error "$smtp_fixture/messages" >"$system_dir/smtp-after-push-recovery.json"
node -e '
  const fixture = require(process.argv[1]);
  for (const [monitor, incident] of [["nightly-push", process.argv[2]], ["state-push", process.argv[3]]]) {
    for (const kind of ["alert", "recovery"]) {
      const messages = fixture.messages.filter(item => item.content.includes(`upaffe ${kind}: system-project/${monitor}`));
      if (messages.length !== 1 || !messages[0].content.includes(incident) || !messages[0].content.includes("Time:")) process.exit(1);
    }
  }
' "$system_dir/smtp-after-push-recovery.json" "$push_job_incident" "$push_state_incident"

# A second restart before the fresh deadlines proves the plans survive. Then
# wait for both interval-plus-tolerance boundaries and observe one synthetic
# missing-report failure for each mode.
push_job_recovery_deadline=$(json_value next_deadline_at <"$system_dir/push-job-browser-recovery.json")
push_state_recovery_deadline=$(json_value next_deadline_at <"$system_dir/push-state-browser-recovery.json")
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up --wait >/dev/null
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project nightly-push --json >"$system_dir/push-job-before-missing.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-before-missing.json"
push_job_restarted_deadline=$(json_value next_deadline_at <"$system_dir/push-job-before-missing.json")
push_state_restarted_deadline=$(json_value next_deadline_at <"$system_dir/push-state-before-missing.json")
node -e '
  if (Date.parse(process.argv[1]) !== Date.parse(process.argv[2])) {
    console.error("job push deadline changed across restart: " + process.argv[1] + " -> " + process.argv[2]);
    process.exit(1);
  }
' "$push_job_recovery_deadline" "$push_job_restarted_deadline"
node -e '
  if (Date.parse(process.argv[1]) !== Date.parse(process.argv[2])) {
    console.error("state push deadline changed across restart: " + process.argv[1] + " -> " + process.argv[2]);
    process.exit(1);
  }
' "$push_state_recovery_deadline" "$push_state_restarted_deadline"

push_missing_ready=false
attempt=0
while [ "$attempt" -lt 60 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" push reports system-project nightly-push --json >"$system_dir/push-job-reports.json"
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" push reports system-project state-push --json >"$system_dir/push-state-reports.json"
  if node -e '
    const job = require(process.argv[1]);
    const state = require(process.argv[2]);
    if (!job.items.some(item => item.reason === "report_missing")) process.exit(1);
    if (!state.items.some(item => item.reason === "report_missing")) process.exit(1);
  ' "$system_dir/push-job-reports.json" "$system_dir/push-state-reports.json"; then
    push_missing_ready=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$push_missing_ready" = "true" ]
echo "both push modes detected a missing report"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project report system-project --json >"$system_dir/project-report-missing.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project report system-project >"$system_dir/project-report-missing.txt"
node -e '
  const report = require(process.argv[1]);
  for (const key of ["nightly-push", "state-push"]) {
    const monitor = report.attention.find(item => item.type === "push" && item.key === key);
    if (!monitor || monitor.latest_result?.reason !== "report_missing" ||
        !monitor.last_success || !monitor.incident || monitor.incident.age_seconds < 0 ||
        !monitor.next_due_at) process.exit(1);
  }
' "$system_dir/project-report-missing.json"
grep 'operator_guidance' "$system_dir/project-report-missing.txt" >/dev/null
grep 'report_missing' "$system_dir/project-report-missing.txt" >/dev/null

# Pause and resume start a fresh untested generation while retaining the open
# missing-report incident. Exercise one mode through the CLI and the other
# through the browser-session operations used by the web application.
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project nightly-push --json >"$system_dir/push-job-missing.json"
push_job_missing_incident=$(json_value open_incident_id <"$system_dir/push-job-missing.json")
push_job_version=$(json_value version <"$system_dir/push-job-missing.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push pause system-project nightly-push --version "$push_job_version" --json \
  >"$system_dir/push-job-pause.json"
push_job_version=$(json_value version <"$system_dir/push-job-pause.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push resume system-project nightly-push --version "$push_job_version" --json \
  >"$system_dir/push-job-resume.json"
[ "$(json_value state <"$system_dir/push-job-resume.json")" = "untested" ]
[ "$(json_value open_incident_id <"$system_dir/push-job-resume.json")" = "$push_job_missing_incident" ]

UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-missing.json"
push_state_missing_incident=$(json_value open_incident_id <"$system_dir/push-state-missing.json")
push_state_version=$(json_value version <"$system_dir/push-state-missing.json")
curl --fail --silent --show-error \
  --cookie "$cookie_jar" --request POST \
  --header 'Content-Type: application/json' --header 'X-Upaffe-CSRF: 1' --header "Origin: $origin" \
  --data-binary "{\"version\":$push_state_version}" \
  "$base_url/api/projects/system-project/push-monitors/state-push/pause" \
  >"$system_dir/push-state-browser-pause.json"
push_state_version=$(json_value version <"$system_dir/push-state-browser-pause.json")
curl --fail --silent --show-error \
  --cookie "$cookie_jar" --request POST \
  --header 'Content-Type: application/json' --header 'X-Upaffe-CSRF: 1' --header "Origin: $origin" \
  --data-binary "{\"version\":$push_state_version}" \
  "$base_url/api/projects/system-project/push-monitors/state-push/resume" \
  >"$system_dir/push-state-browser-resume.json"
[ "$(json_value state <"$system_dir/push-state-browser-resume.json")" = "untested" ]
[ "$(json_value open_incident_id <"$system_dir/push-state-browser-resume.json")" = "$push_state_missing_incident" ]

# Only new-generation reports resolve the retained incidents.
push_job_fresh_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_state_fresh_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_fresh_at=$(node -e 'process.stdout.write(new Date().toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_job_fresh_id" "$push_fresh_at" >"$system_dir/push-job-fresh-input.json"
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_state_fresh_id" "$push_fresh_at" >"$system_dir/push-state-fresh-input.json"
curl --fail --silent --show-error --request POST \
  --header "Authorization: Bearer $push_job_token" --header 'Content-Type: application/json' \
  --data-binary @"$system_dir/push-job-fresh-input.json" "$base_url/api/reports" \
  >"$system_dir/push-job-fresh-receipt.json"
curl --fail --silent --show-error --request POST \
  --header "Authorization: Bearer $push_state_token" --header 'Content-Type: application/json' \
  --data-binary @"$system_dir/push-state-fresh-input.json" "$base_url/api/reports" \
  >"$system_dir/push-state-fresh-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project nightly-push --json >"$system_dir/push-job-fresh.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project state-push --json >"$system_dir/push-state-fresh.json"
[ "$(json_value state <"$system_dir/push-job-fresh.json")" = "healthy" ]
[ "$(json_value state <"$system_dir/push-state-fresh.json")" = "healthy" ]
echo "push pause and resume started fresh windows"

# Reporting rotation accepts both job secrets during overlap. Revocation then
# rejects the old token and the rotated secret URL immediately. The state
# credential is revoked independently and its secret URL is rejected too.
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential rotate system-project nightly-push --json \
  >"$system_dir/push-job-credential-rotated.json"
push_job_rotated_token=$(json_value token <"$system_dir/push-job-credential-rotated.json")
push_job_rotated_url=$(json_value report_url <"$system_dir/push-job-credential-rotated.json")
push_job_rotated_url_secret=${push_job_rotated_url##*/}
push_job_overlap_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
push_job_overlap_at=$(node -e 'process.stdout.write(new Date().toISOString())')
node -e '
  process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2], outcome: "success", reason: null }));
' "$push_job_overlap_id" "$push_job_overlap_at" >"$system_dir/push-job-overlap-input.json"
curl --fail --silent --show-error --request POST \
  --header "Authorization: Bearer $push_job_token" --header 'Content-Type: application/json' \
  --data-binary @"$system_dir/push-job-overlap-input.json" "$base_url/api/reports" \
  >"$system_dir/push-job-overlap-receipt.json"
curl --fail --silent --show-error --request POST "${base_url}${push_job_rotated_url}" \
  >"$system_dir/push-job-rotated-simple.txt"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential revoke system-project nightly-push --json \
  >"$system_dir/push-job-credential-revoked.json"

push_revoked_status=$(curl --silent --show-error --output "$system_dir/push-job-revoked.json" \
  --write-out '%{http_code}' --request POST "${base_url}${push_job_rotated_url}")
[ "$push_revoked_status" = "401" ]
grep 'reporting_rejected' "$system_dir/push-job-revoked.json" >/dev/null
push_revoked_old_status=$(curl --silent --show-error --output "$system_dir/push-job-old-revoked.json" \
  --write-out '%{http_code}' --request POST \
  --header "Authorization: Bearer $push_job_token" --header 'Content-Type: application/json' \
  --data-binary @"$system_dir/push-job-overlap-input.json" "$base_url/api/reports")
[ "$push_revoked_old_status" = "401" ]

UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential revoke system-project state-push --json \
  >"$system_dir/push-state-credential-revoked.json"
push_state_revoked_status=$(curl --silent --show-error --output "$system_dir/push-state-revoked.json" \
  --write-out '%{http_code}' --request POST "${base_url}${push_state_url}")
[ "$push_state_revoked_status" = "401" ]
grep 'reporting_rejected' "$system_dir/push-state-revoked.json" >/dev/null
echo "push credential rotation and revocation passed"

# One push monitor exercises permanent relay refusal, a retry surviving restart,
# and a queued alert made obsolete by a prompt recovery.
smoke_phase='email retry and obsolete alert'
echo '{"key":"mail-push","name":"Mail proof","mode":"job_completion","interval_seconds":3600,"tolerance_seconds":0,"instruction":"Inspect the fictional job","runbook_url":"https://docs.example.test/runbooks/mail-proof"}' >"$system_dir/mail-push-create-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push create system-project --file "$system_dir/mail-push-create-input.json" --json \
  >"$system_dir/mail-push-create.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential issue system-project mail-push --json >"$system_dir/mail-push-credential.json"
mail_push_token=$(json_value token <"$system_dir/mail-push-credential.json")

send_mail_report() {
  report_token=$1
  report_outcome=$2
  report_file=$3
  report_id=$(node -e 'process.stdout.write(require("node:crypto").randomUUID())')
  report_at=$(node -e 'process.stdout.write(new Date().toISOString())')
  node -e '
    process.stdout.write(JSON.stringify({ report_id: process.argv[1], observed_at: process.argv[2],
      outcome: process.argv[3], reason: process.argv[3] === "failure" ? "fictional job failed" : null }));
  ' "$report_id" "$report_at" "$report_outcome" >"$report_file.input"
  curl --fail --silent --show-error --request POST \
    --header "Authorization: Bearer $report_token" --header 'Content-Type: application/json' \
    --data-binary @"$report_file.input" "$base_url/api/reports" >"$report_file"
}

wait_email_state() {
  wanted_incident=$1
  wanted_kind=$2
  wanted_state=$3
  wanted_file=$4
  attempts=0
  while [ "$attempts" -lt 100 ]; do
    UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
      "$ua" email incident "$wanted_incident" --monitor-type push --json >"$wanted_file"
    if node -e '
      const status = require(process.argv[1]);
      const matches = status.deliveries.filter(item => item.kind === process.argv[2] && item.state === process.argv[3]);
      if (matches.length !== 1) process.exit(1);
    ' "$wanted_file" "$wanted_kind" "$wanted_state"; then return 0; fi
    attempts=$((attempts + 1))
    sleep 1
  done
  return 1
}

curl --fail --silent --show-error --request POST "$smtp_fixture/mode/reject" >"$system_dir/smtp-reject-mode.json"
send_mail_report "$mail_push_token" failure "$system_dir/mail-terminal-failure-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project mail-push --json >"$system_dir/mail-terminal-open.json"
terminal_incident=$(json_value open_incident_id <"$system_dir/mail-terminal-open.json")
wait_email_state "$terminal_incident" alert terminal_failure "$system_dir/mail-terminal-status.json"
node -e '
  const status = require(process.argv[1]);
  const row = status.deliveries[0];
  if (row.attempt_count !== 1 || row.last_error_code !== "smtp_rejected" || status.announcement_state !== "failed") process.exit(1);
' "$system_dir/mail-terminal-status.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email summary --project system-project --json >"$system_dir/mail-failure-summary.json"
node -e 'if (require(process.argv[1]).terminal_failure_count < 1) process.exit(1)' "$system_dir/mail-failure-summary.json"
sleep 1
send_mail_report "$mail_push_token" success "$system_dir/mail-terminal-recovery-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email incident "$terminal_incident" --monitor-type push --json >"$system_dir/mail-terminal-resolved.json"
node -e '
  const status = require(process.argv[1]);
  if (status.deliveries.some(item => item.kind === "recovery")) process.exit(1);
' "$system_dir/mail-terminal-resolved.json"

curl --fail --silent --show-error --request POST "$smtp_fixture/mode/temporary" >"$system_dir/smtp-temporary-mode.json"
sleep 1
send_mail_report "$mail_push_token" failure "$system_dir/mail-retry-failure-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project mail-push --json >"$system_dir/mail-retry-open.json"
retry_incident=$(json_value open_incident_id <"$system_dir/mail-retry-open.json")
wait_email_state "$retry_incident" alert retrying "$system_dir/mail-retrying-status.json"
node -e '
  const row = require(process.argv[1]).deliveries[0];
  if (row.attempt_count !== 1 || row.last_error_code !== "smtp_rejected" || !row.next_attempt_at) process.exit(1);
' "$system_dir/mail-retrying-status.json"
curl --fail --silent --show-error --cookie "$cookie_jar" \
  "$base_url/api/email/incidents/$retry_incident?monitor_type=push" >"$system_dir/mail-retrying-browser.json"
node -e '
  const cli = require(process.argv[1]); const browser = require(process.argv[2]);
  if (cli.announcement_state !== browser.announcement_state || cli.deliveries[0].state !== browser.deliveries[0].state) process.exit(1);
' "$system_dir/mail-retrying-status.json" "$system_dir/mail-retrying-browser.json"
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up --wait >/dev/null
curl --fail --silent --show-error --request POST "$smtp_fixture/mode/accept" >"$system_dir/smtp-accept-mode.json"
wait_email_state "$retry_incident" alert accepted "$system_dir/mail-retry-accepted.json"
echo "transient SMTP alert accepted after restart"
node -e '
  const row = require(process.argv[1]).deliveries[0];
  if (row.attempt_count !== 2 || !row.accepted_at) process.exit(1);
' "$system_dir/mail-retry-accepted.json"
sleep 1
send_mail_report "$mail_push_token" success "$system_dir/mail-retry-recovery-receipt.json"
wait_email_state "$retry_incident" recovery accepted "$system_dir/mail-retry-recovered.json"
echo "transient SMTP incident recovered"

curl --fail --silent --show-error --request POST "$smtp_fixture/mode/temporary" >"$system_dir/smtp-stale-mode.json"
sleep 1
send_mail_report "$mail_push_token" failure "$system_dir/mail-stale-failure-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project mail-push --json >"$system_dir/mail-stale-open.json"
stale_incident=$(json_value open_incident_id <"$system_dir/mail-stale-open.json")
wait_email_state "$stale_incident" alert retrying "$system_dir/mail-stale-retrying.json"
sleep 1
send_mail_report "$mail_push_token" success "$system_dir/mail-stale-recovery-receipt.json"
wait_email_state "$stale_incident" alert obsolete "$system_dir/mail-stale-obsolete.json"
echo "resolved queued alert became obsolete"
curl --fail --silent --show-error --request POST "$smtp_fixture/mode/accept" >"$system_dir/smtp-after-stale-mode.json"

# Project maintenance and direct monitor maintenance overlap. Reports and an
# HTTP check continue during suppression; a closed episode stays silent while
# a still-open episode becomes eligible once both scopes have ended.
smoke_phase='timed maintenance'
echo '{"key":"quiet-push","name":"Quiet maintenance","mode":"job_completion","interval_seconds":3600,"tolerance_seconds":0,"instruction":"Inspect the fictional job","runbook_url":"https://docs.example.test/runbooks/quiet-proof"}' >"$system_dir/quiet-push-create-input.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push create system-project --file "$system_dir/quiet-push-create-input.json" --json >"$system_dir/quiet-push-create.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push credential issue system-project quiet-push --json >"$system_dir/quiet-push-credential.json"
quiet_push_token=$(json_value token <"$system_dir/quiet-push-credential.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" maintenance start system-project --scope project --version 0 --duration-seconds 60 --json \
  >"$system_dir/mail-project-maintenance-start.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" maintenance start system-project mail-push --scope push --version 0 --duration-seconds 120 --json \
  >"$system_dir/mail-monitor-maintenance-start.json"
curl --fail --silent --show-error --cookie "$cookie_jar" \
  "$base_url/api/projects/system-project/push-monitors/mail-push/maintenance" \
  >"$system_dir/mail-monitor-maintenance-browser.json"
node -e '
  const cli = require(process.argv[1]); const browser = require(process.argv[2]);
  if (!cli.effective_active || !browser.effective_active || Date.parse(cli.effective_ends_at) !== Date.parse(browser.effective_ends_at) || browser.active_scopes.length !== 2) process.exit(1);
' "$system_dir/mail-monitor-maintenance-start.json" "$system_dir/mail-monitor-maintenance-browser.json"
echo "overlapping maintenance started"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" project report system-project --json >"$system_dir/project-report-maintenance.json"
node -e '
  const report = require(process.argv[1]);
  const monitor = report.healthy.find(item => item.type === "push" && item.key === "mail-push");
  if (!report.project_maintenance || !monitor || !monitor.direct_maintenance ||
      !monitor.effective_maintenance_until || report.email.host !== "smtp-fixture" ||
      !report.email.has_password || report.email.delivery.terminal_failure_count < 1) process.exit(1);
' "$system_dir/project-report-maintenance.json"

UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" monitor test system-project public-homepage --json >"$system_dir/mail-maintenance-http-check.json"
[ "$(json_value succeeded <"$system_dir/mail-maintenance-http-check.json")" = "true" ]
send_mail_report "$quiet_push_token" failure "$system_dir/mail-quiet-failure-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project quiet-push --json >"$system_dir/mail-quiet-open.json"
quiet_incident=$(json_value open_incident_id <"$system_dir/mail-quiet-open.json")
sleep 1
send_mail_report "$quiet_push_token" success "$system_dir/mail-quiet-recovery-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email incident "$quiet_incident" --monitor-type push --json >"$system_dir/mail-quiet-status.json"
node -e '
  const status = require(process.argv[1]);
  if (status.open || status.deliveries.length !== 0 || status.announcement_state !== "silent") process.exit(1);
' "$system_dir/mail-quiet-status.json"
sleep 1
send_mail_report "$mail_push_token" failure "$system_dir/mail-held-failure-receipt.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" push get system-project mail-push --json >"$system_dir/mail-held-open.json"
held_incident=$(json_value open_incident_id <"$system_dir/mail-held-open.json")
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email incident "$held_incident" --monitor-type push --json >"$system_dir/mail-held-suppressed.json"
node -e '
  const status = require(process.argv[1]);
  if (!status.open || status.announcement_state !== "suppressed" || status.deliveries.length !== 0) process.exit(1);
' "$system_dir/mail-held-suppressed.json"
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" restart app >/dev/null
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" up --wait >/dev/null

project_maintenance_expired=false
attempt=0
while [ "$attempt" -lt 90 ]; do
  UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
    "$ua" maintenance get system-project --scope project --json >"$system_dir/mail-project-maintenance-expired.json"
  if [ "$(json_value effective_active <"$system_dir/mail-project-maintenance-expired.json")" = "false" ]; then
    project_maintenance_expired=true
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done
[ "$project_maintenance_expired" = "true" ]
echo "project maintenance expired after restart"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" maintenance get system-project mail-push --scope push --json >"$system_dir/mail-monitor-still-active.json"
[ "$(json_value effective_active <"$system_dir/mail-monitor-still-active.json")" = "true" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email incident "$held_incident" --monitor-type push --json >"$system_dir/mail-held-after-project-expiry.json"
[ "$(json_value announcement_state <"$system_dir/mail-held-after-project-expiry.json")" = "suppressed" ]
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" maintenance end system-project mail-push --scope push --version 1 --json \
  >"$system_dir/mail-monitor-maintenance-end.json"
wait_email_state "$held_incident" alert accepted "$system_dir/mail-held-accepted.json"
UPAFFE_URL="$base_url" UPAFFE_CREDENTIAL="$credential_token" \
  "$ua" email incident "$quiet_incident" --monitor-type push --json >"$system_dir/mail-quiet-after-expiry.json"
node -e '
  const status = require(process.argv[1]);
  if (status.deliveries.length !== 0 || status.announcement_state !== "silent") process.exit(1);
' "$system_dir/mail-quiet-after-expiry.json"
sleep 1
send_mail_report "$mail_push_token" success "$system_dir/mail-held-recovery-receipt.json"
wait_email_state "$held_incident" recovery accepted "$system_dir/mail-held-recovered.json"
curl --fail --silent --show-error "$smtp_fixture/messages" >"$system_dir/smtp-final.json"
node -e '
  const fixture = require(process.argv[1]);
  for (const id of [process.argv[2], process.argv[3], process.argv[4]]) {
    if (fixture.messages.some(item => item.content.includes(id))) process.exit(1);
  }
  for (const id of [process.argv[5], process.argv[6]]) {
    const messages = fixture.messages.filter(item => item.content.includes(id));
    if (messages.length !== 2) process.exit(1);
  }
' "$system_dir/smtp-final.json" "$terminal_incident" "$stale_incident" "$quiet_incident" "$retry_incident" "$held_incident"
echo "SMTP refusal, restart retry, obsolete alert, and overlapping maintenance passed"

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
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" exec -T db \
  psql -U upaffe -d upaffe -Atc 'select count(*) from operator_identity')
project_count=$(UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" exec -T db \
  psql -U upaffe -d upaffe -Atc "select count(*) from project where key = 'system-project'")
[ "$operator_count" = "1" ]
[ "$project_count" = "1" ]

UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" logs --no-color app >"$system_dir/app.log"
UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" -f "$smoke_compose_file" logs --no-color smtp-fixture >"$system_dir/smtp.log"

# Explicit create/rotate files are the only allowed token-revealing artifacts and are excluded here.
ordinary_files="
$system_dir/web.html
$system_dir/bootstrap-state.json
$system_dir/bootstrap-second.json
$system_dir/bootstrap-project.json
$system_dir/sign-in-response.txt
$system_dir/session.json
$system_dir/project-browser-create.json
$system_dir/project-cli-get.json
$system_dir/project-cli-create.json
$system_dir/project-cli-create-repeat.json
$system_dir/project-cli-rename.json
$system_dir/project-stale-output.json
$system_dir/project-stale-error.json
$system_dir/project-api-delete.json
$system_dir/project-browser-deleted.json
$system_dir/project-browser-restore.json
$system_dir/project-old-token.json
$system_dir/project-new-token.json
$system_dir/monitor-cli-create.json
$system_dir/monitor-cli-create-repeat.json
$system_dir/monitor-scheduled-checks.json
$system_dir/monitor-cli-test-success.json
$system_dir/monitor-before-planning-restart.json
$system_dir/monitor-after-planning-restart.json
$system_dir/monitor-failing-update.json
$system_dir/monitor-first-failure.json
$system_dir/monitor-first-failure-diagnostic.txt
$system_dir/monitor-below-threshold.json
$system_dir/project-report-below-threshold.json
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
$system_dir/push-job-create.json
$system_dir/push-job-create-repeat.json
$system_dir/push-state-create.json
$system_dir/push-job-issue-again-output.json
$system_dir/push-job-issue-again-error.json
$system_dir/push-reporting-token-read.txt
$system_dir/push-reporting-token-read-diagnostic.txt
$system_dir/push-state-simple-success.txt
$system_dir/push-state-after-simple.json
$system_dir/push-job-success-receipt.json
$system_dir/push-state-after-job-report.json
$system_dir/push-job-failure-receipt.json
$system_dir/push-job-duplicate-receipt.json
$system_dir/push-job-repeat-receipt.json
$system_dir/push-job-old-success-receipt.json
$system_dir/push-job-open.json
$system_dir/push-job-one-incident.json
$system_dir/push-state-failure-receipt.json
$system_dir/push-state-repeat-receipt.json
$system_dir/push-state-open.json
$system_dir/push-state-one-incident.json
$system_dir/push-job-after-open-restart.json
$system_dir/push-state-after-open-restart.json
$system_dir/push-job-recovery-receipt.json
$system_dir/push-state-recovery-receipt.json
$system_dir/push-job-browser-recovery.json
$system_dir/push-state-browser-recovery.json
$system_dir/push-job-before-missing.json
$system_dir/push-state-before-missing.json
$system_dir/push-job-reports.json
$system_dir/push-state-reports.json
$system_dir/project-report-missing.json
$system_dir/project-report-missing.txt
$system_dir/push-job-missing.json
$system_dir/push-job-pause.json
$system_dir/push-job-resume.json
$system_dir/push-state-missing.json
$system_dir/push-state-browser-pause.json
$system_dir/push-state-browser-resume.json
$system_dir/push-job-fresh-receipt.json
$system_dir/push-state-fresh-receipt.json
$system_dir/push-job-fresh.json
$system_dir/push-state-fresh.json
$system_dir/push-job-overlap-receipt.json
$system_dir/push-job-rotated-simple.txt
$system_dir/push-job-credential-revoked.json
$system_dir/push-job-revoked.json
$system_dir/push-job-old-revoked.json
$system_dir/push-state-credential-revoked.json
$system_dir/push-state-revoked.json
$system_dir/project-report-maintenance.json
$system_dir/revoke-response.txt
$system_dir/revoked-output.txt
$system_dir/revoked-diagnostic.txt
$system_dir/revoked-old-output.txt
$system_dir/revoked-old-diagnostic.txt
$system_dir/app.log
$system_dir/smtp.log
"
email_ordinary_files=$(find "$system_dir" -maxdepth 1 -type f \( -name 'email-*.json' -o -name 'mail-*.json' -o -name 'smtp-*.json' \) ! -name '*input*' ! -name '*credential*')
ordinary_files="$ordinary_files
$email_ordinary_files"
# The newline-delimited variable is intentionally split into path arguments.
assert_absent "$operator_password" $ordinary_files
assert_absent "$initial_token" $ordinary_files
assert_absent "$session_secret" $ordinary_files
assert_absent "$credential_token" $ordinary_files
assert_absent "$rotated_token" $ordinary_files
assert_absent "$monitor_header_secret" $ordinary_files
assert_absent "$push_job_token" $ordinary_files
assert_absent "$push_job_url_secret" $ordinary_files
assert_absent "$push_job_url_secret" "$system_dir/push-malformed-report.json"
assert_absent "$push_state_token" $ordinary_files
assert_absent "$push_state_url_secret" $ordinary_files
assert_absent "$push_job_rotated_token" $ordinary_files
assert_absent "$push_job_rotated_url_secret" $ordinary_files
assert_absent "$smtp_password" $ordinary_files
assert_absent "$mail_push_token" $ordinary_files
assert_absent "$quiet_push_token" $ordinary_files

echo "access, project, HTTP, push, email, and maintenance system test passed"
