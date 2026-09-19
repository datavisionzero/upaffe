#!/bin/sh
# Rehearse installation, update, failure, backup, and restore with published images.
set -eu
umask 077
if [ "$#" -lt 2 ] || [ "$#" -gt 3 ] || [ "$1" = "$2" ]; then
  echo 'Usage: scripts/check-production-workflow.sh PREVIOUS_IMAGE CURRENT_IMAGE [EXPECTED_CURRENT_VERSION]' >&2
  exit 2
fi
for image in "$1" "$2"; do
  case "$image" in
    ghcr.io/datavisionzero/upaffe:sha-*|ghcr.io/datavisionzero/upaffe:v*|ghcr.io/datavisionzero/upaffe@sha256:*) ;;
    *) echo 'Expected a published upaffe GHCR revision, release tag, or digest.' >&2; exit 2 ;;
  esac
done
previous_image=$1
current_image=$2
expected_current_version=${3:-}
if [ -z "$expected_current_version" ]; then
  case "$current_image" in
    ghcr.io/datavisionzero/upaffe:sha-*) expected_current_version="0.0.0-rev.${current_image##*:sha-}" ;;
    ghcr.io/datavisionzero/upaffe:v*) expected_current_version=${current_image##*:v} ;;
    *) echo 'A digest reference requires EXPECTED_CURRENT_VERSION.' >&2; exit 2 ;;
  esac
fi
root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
"$root/scripts/check-production-heartbeat.sh" "$current_image"
check_image=$previous_image
mkdir -p "$root/scratchpad"
check_dir=$(mktemp -d "$root/scratchpad/upaffe-workflow.XXXXXX")
project_suffix=$(openssl rand -hex 6)
check_project="upaffe-workflow-$project_suffix"
restore_project="upaffe-restore-$project_suffix"
restore_port=${UPAFFE_RESTORE_TEST_PORT:-18084}
check_port=${UPAFFE_WORKFLOW_TEST_PORT:-18083}
cleanup() {
  if [ -d "$check_dir/restored" ]; then
    (cd "$check_dir/restored" && UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$restore_port" \
      docker compose -p "$restore_project" -f docker-compose.yml -f docker-compose.verify-restore.yml down --volumes) >/dev/null 2>&1 || true
  fi
  (cd "$check_dir" && UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
    docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml down --volumes) >/dev/null 2>&1 || true
  rm -rf -- "$check_dir"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM
cp "$root/deploy/docker-compose.yml" "$root/deploy/docker-compose.bootstrap.yml" \
  "$root/deploy/docker-compose.verify-restore.yml" "$root/deploy/backup-production.sh" \
  "$root/deploy/restore-production.sh" "$check_dir/"
write_env() {
  printf 'UPAFFE_IMAGE=%s\nUPAFFE_PORT=%s\nUPAFFE_POSTGRES_IMAGE=postgres:18\n' \
    "$1" "$check_port" > "$check_dir/.env"
}
write_env "$check_image"
cat > "$check_dir/docker-compose.test.yml" <<'TEST_OVERLAY'
services:
  app:
    environment:
      EmailDelivery__Enabled: "false"
TEST_OVERLAY
mkdir -m 0700 "$check_dir/secrets"
openssl rand -hex 24 > "$check_dir/secrets/postgres_password"
bootstrap_proof=$(openssl rand -hex 32)
operator_password=$(openssl rand -hex 24)
printf '%s\n' "$bootstrap_proof" > "$check_dir/secrets/bootstrap_proof"
jq -n --arg proof "$bootstrap_proof" --arg email operator@example.test \
  --arg password "$operator_password" \
  '{proof:$proof,email:$email,password:$password}' > "$check_dir/bootstrap-request.json"
jq -n --arg email operator@example.test --arg password "$operator_password" \
  '{email:$email,password:$password}' > "$check_dir/session-request.json"
chmod 0644 "$check_dir/secrets/postgres_password" "$check_dir/secrets/bootstrap_proof"
cd "$check_dir"
echo 'Workflow: empty installation and one-time operator bootstrap.'
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.bootstrap.yml -f docker-compose.test.yml up -d --wait
curl --fail --silent "http://127.0.0.1:$check_port/api/health/ready" | grep -q '"status":"ready"'
curl --fail --silent "http://127.0.0.1:$check_port/api/bootstrap" | grep -q '"required":true,"available":true'
status=$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data-binary @bootstrap-request.json \
  "http://127.0.0.1:$check_port/api/bootstrap")
[ "$status" = 204 ]
base_url="http://127.0.0.1:$check_port"
curl --fail --silent --cookie-jar browser.cookies \
  --header 'Content-Type: application/json' \
  --data-binary @session-request.json \
  "$base_url/api/session" >/dev/null
curl --fail --silent --cookie browser.cookies \
  --header 'Content-Type: application/json' --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $base_url" \
  --data '{"key":"fixture-project","name":"Fixture project"}' \
  "$base_url/api/projects" > project.json
project_id=$(jq -r .id project.json)
[ "$project_id" != null ]
curl --fail --silent --cookie browser.cookies \
  --header 'Content-Type: application/json' --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $base_url" \
  --request PUT --data '{"version":1,"recipients":["ops@example.test"]}' \
  "$base_url/api/projects/fixture-project/recipients" > recipients.json
curl --fail --silent --cookie browser.cookies \
  --header 'Content-Type: application/json' --header 'X-Upaffe-CSRF: 1' \
  --header "Origin: $base_url" \
  --data '{"key":"backup","name":"Backup","mode":"job_completion","interval_seconds":3600,"tolerance_seconds":0}' \
  "$base_url/api/projects/fixture-project/push-monitors" > monitor.json
monitor_id=$(jq -r .id monitor.json)
[ "$monitor_id" != null ]
curl --fail --silent --cookie browser.cookies \
  --header 'X-Upaffe-CSRF: 1' --header "Origin: $base_url" \
  --request POST \
  "$base_url/api/projects/fixture-project/push-monitors/backup/reporting-credential" > reporting-credential.json
reporting_token=$(jq -r .token reporting-credential.json)
report_id=$(python3 -c 'import uuid; print(uuid.uuid4())')
observed_at=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
jq -n --arg id "$report_id" --arg at "$observed_at" \
  '{report_id:$id,observed_at:$at,outcome:"failure",reason:"fixture backup failed"}' > report.json
curl --fail --silent --header "Authorization: Bearer $reporting_token" \
  --header 'Content-Type: application/json' --data-binary @report.json \
  "$base_url/api/reports" > receipt.json
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup" > monitor-before.json
incident_id=$(jq -r .open_incident_id monitor-before.json)
deadline=$(jq -r .next_deadline_at monitor-before.json)
[ "$incident_id" != null ] && [ "$deadline" != null ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup/reports" > report-list-before.json
internal_report_id=$(jq -r --arg id "$report_id" '.items[] | select(.report_id == $id) | .id' report-list-before.json)
[ -n "$internal_report_id" ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup/reports/$internal_report_id" > report-before.json
[ "$(jq -r .report_id report-before.json)" = "$report_id" ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/email/deliveries/summary" > delivery-before.json
[ "$(jq -r .pending_count delivery-before.json)" = 1 ]
echo 'Workflow: recreate application without the bootstrap proof.'
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml up -d --wait
rm secrets/bootstrap_proof
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml up -d --wait --force-recreate
curl --fail --silent "http://127.0.0.1:$check_port/api/bootstrap" | grep -q '"required":false,"available":false'
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project" > project-after.json
[ "$(jq -r .id project-after.json)" = "$project_id" ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup" > monitor-after.json
[ "$(jq -r .id monitor-after.json)" = "$monitor_id" ]
[ "$(jq -r .open_incident_id monitor-after.json)" = "$incident_id" ]
[ "$(jq -r .next_deadline_at monitor-after.json)" = "$deadline" ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/email/deliveries/summary" > delivery-after.json
[ "$(jq -r .pending_count delivery-after.json)" = 1 ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup/reports/$internal_report_id" > report-after.json
[ "$(jq -r .report_id report-after.json)" = "$report_id" ]
echo 'Workflow: pre-upgrade backup and failed image pull rollback.'
./backup-production.sh "$check_dir/pre-upgrade" "$check_project"
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml stop app
write_env 'ghcr.io/datavisionzero/upaffe:sha-fictional-missing-revision'
if docker compose -p "$check_project" -f docker-compose.yml pull app >/dev/null 2>&1; then
  echo 'Missing image unexpectedly pulled.' >&2; exit 1
fi
cp "$check_dir/pre-upgrade/.env" .env
cp "$check_dir/pre-upgrade/docker-compose.yml" .
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml up -d --wait app
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project" > project-after-failed-upgrade.json
[ "$(jq -r .id project-after-failed-upgrade.json)" = "$project_id" ]
./backup-production.sh "$check_dir/before-successful-update" "$check_project"
echo 'Workflow: controlled update to the current published image.'
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml stop app
target_image=$current_image
write_env "$target_image"
UPAFFE_PORT="$check_port" docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml config --quiet
UPAFFE_PORT="$check_port" docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml pull app
UPAFFE_PORT="$check_port" docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml up -d --wait app
curl --fail --silent --retry 6 --retry-delay 2 "$base_url/api/health/progress" > progress-after-update.json
curl --fail --silent --show-error --dump-header version-after-update.headers \
  "$base_url/api/version" > version-after-update.json
[ "$(jq -r .version version-after-update.json)" = "$expected_current_version" ]
[ "$(awk 'tolower($1) == "upaffe-version:" {gsub(/\r/, "", $2); print $2}' \
  version-after-update.headers)" = "$expected_current_version" ]
curl --fail --silent --cookie browser.cookies "$base_url/api/projects/fixture-project" > project-after-update.json
[ "$(jq -r .id project-after-update.json)" = "$project_id" ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup" > monitor-after-update.json
[ "$(jq -r .id monitor-after-update.json)" = "$monitor_id" ]
[ "$(jq -r .open_incident_id monitor-after-update.json)" = "$incident_id" ]
[ "$(jq -r .next_deadline_at monitor-after-update.json)" = "$deadline" ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/email/deliveries/summary" > delivery-after-update.json
[ "$(jq -r .pending_count delivery-after-update.json)" = 1 ]
curl --fail --silent --cookie browser.cookies \
  "$base_url/api/projects/fixture-project/push-monitors/backup/reports/$internal_report_id" > report-after-update.json
[ "$(jq -r .report_id report-after-update.json)" = "$report_id" ]
check_image=$target_image
echo 'Workflow: restore pre-upgrade state after an incompatible schema.'
UPAFFE_IMAGE="$check_image" UPAFFE_PORT="$check_port" \
  docker compose -p "$check_project" -f docker-compose.yml stop app >/dev/null
docker compose -p "$check_project" -f docker-compose.yml exec -T db \
  psql -v ON_ERROR_STOP=1 -U upaffe -d upaffe >/dev/null <<'SQL'
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20990101000000_FixtureFuture', '10.0.0');
SQL
write_env "$previous_image"
if docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml \
  up -d --wait --wait-timeout 30 app >schema-start.log 2>&1; then
  echo 'Older image unexpectedly accepted a future migration.' >&2; exit 1
fi
if ! docker compose -p "$check_project" -f docker-compose.yml -f docker-compose.test.yml \
  logs app 2>/dev/null | grep -qi 'newer upaffe'; then
  echo 'Schema mismatch did not produce the expected startup diagnostic.' >&2; exit 1
fi
if [ ! -s "$check_dir/before-successful-update/database.dump" ]; then
  echo 'Pre-upgrade backup is empty.' >&2; exit 1
fi
if ./backup-production.sh "$check_dir/before-successful-update" "$check_project" >/dev/null 2>&1; then
  echo 'Existing backup destination was accepted.' >&2; exit 1
fi
./restore-production.sh "$check_dir/before-successful-update" "$check_dir/restored" "$restore_project"
source_future_count=$(docker compose -p "$check_project" -f docker-compose.yml exec -T db \
  psql -t -A -U upaffe -d upaffe <<'SQL'
SELECT count(*) FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20990101000000_FixtureFuture';
SQL
)
[ "$source_future_count" = 1 ]
if ./restore-production.sh "$check_dir/before-successful-update" "$check_dir/restored" "$restore_project" >/dev/null 2>&1; then
  echo 'Existing restore destination was accepted.' >&2; exit 1
fi
if ./restore-production.sh "$check_dir/before-successful-update" "$check_dir/another-restore" "$check_project" >/dev/null 2>&1; then
  echo 'Existing source project was accepted for restore.' >&2; exit 1
fi
cp -R "$check_dir/before-successful-update" "$check_dir/tampered"
printf x >> "$check_dir/tampered/database.dump"
if ./restore-production.sh "$check_dir/tampered" "$check_dir/tampered-restore" "$restore_project" >/dev/null 2>&1; then
  echo 'Tampered archive was accepted.' >&2; exit 1
fi
cd "$check_dir/restored"
UPAFFE_PORT="$restore_port" UPAFFE_TRUSTED_PROXY_IPS='' UPAFFE_PUBLIC_ORIGIN='' \
  docker compose -p "$restore_project" -f docker-compose.yml -f docker-compose.verify-restore.yml up -d --wait app
restored_url="http://127.0.0.1:$restore_port"
curl --fail --silent "$restored_url/api/health/ready" | grep -q '"status":"ready"'
curl --fail --silent "$restored_url/api/bootstrap" | grep -q '"required":false,"available":false'
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$restored_url/api/health/progress")" = 503 ]
curl --fail --silent --cookie-jar restored.cookies \
  --header 'Content-Type: application/json' \
  --data-binary @"$check_dir/session-request.json" \
  "$restored_url/api/session" >/dev/null
curl --fail --silent --cookie restored.cookies \
  "$restored_url/api/projects/fixture-project" > restored-project.json
[ "$(jq -r .id restored-project.json)" = "$project_id" ]
curl --fail --silent --cookie restored.cookies \
  "$restored_url/api/projects/fixture-project/push-monitors/backup" > restored-monitor.json
[ "$(jq -r .id restored-monitor.json)" = "$monitor_id" ]
[ "$(jq -r .open_incident_id restored-monitor.json)" = "$incident_id" ]
[ "$(jq -r .next_deadline_at restored-monitor.json)" = "$deadline" ]
curl --fail --silent --cookie restored.cookies \
  "$restored_url/api/email/deliveries/summary" > restored-delivery.json
[ "$(jq -r .pending_count restored-delivery.json)" = 1 ]
curl --fail --silent --cookie restored.cookies \
  "$restored_url/api/projects/fixture-project/push-monitors/backup/reports/$internal_report_id" > restored-report.json
[ "$(jq -r .report_id restored-report.json)" = "$report_id" ]
if docker compose -p "$restore_project" -f docker-compose.yml -f docker-compose.verify-restore.yml \
  logs app 2>/dev/null | grep -Fq "$reporting_token"; then
  echo 'Reporting credential appeared in application logs.' >&2
  exit 1
fi
echo 'Production workflow passed: bootstrap, persistence, update, failed pull, schema mismatch, and isolated restore.'
