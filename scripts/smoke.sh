#!/bin/sh
set -eu

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
smoke_project="upaffe-smoke-$$"
smoke_app_port=${UPAFFE_SMOKE_APP_PORT:-18080}
smoke_db_port=${UPAFFE_SMOKE_DB_PORT:-15432}
compose_file="$root/deploy/docker-compose.dev.yml"

cleanup() {
  UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
    docker compose -p "$smoke_project" -f "$compose_file" down --volumes
}
trap cleanup EXIT

UPAFFE_DEV_PORT="$smoke_app_port" UPAFFE_DEV_DB_PORT="$smoke_db_port" \
  docker compose -p "$smoke_project" -f "$compose_file" up --build --wait

base_url="http://localhost:$smoke_app_port"

curl --fail --silent --show-error "$base_url/" | grep '<div id="root"></div>'
curl --fail --silent --show-error "$base_url/api/health/live" | grep '"status":"live"'
curl --fail --silent --show-error "$base_url/api/health/ready" | grep '"status":"ready"'
curl --fail --silent --show-error "$base_url/api/version" | grep '"version":"0.0.0-dev"'

go -C "$root/src/cli" generate ./...
UPAFFE_URL="$base_url" go -C "$root/src/cli" run ./cmd/ua status --json \
  | grep '"version":"0.0.0-dev"'
