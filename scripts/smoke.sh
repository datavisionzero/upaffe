#!/bin/sh
set -eu

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
smoke_project="upaffe-smoke-$$"
smoke_app_port=${UPAFFE_SMOKE_APP_PORT:-18080}
smoke_db_port=${UPAFFE_SMOKE_DB_PORT:-15432}
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

echo "access and project system test passed"
