#!/usr/bin/env bash
set -euo pipefail

image="${1:?Pass a locally built application image tag}"
run_id="${GITHUB_RUN_ID:-local}-$$"
network="upaffe-image-${run_id}"
database="upaffe-image-db-${run_id}"
application="upaffe-image-app-${run_id}"
password='image-check-only'

cleanup() {
  docker rm -f "$application" "$database" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker network create "$network" >/dev/null
docker run -d --name "$database" --network "$network" \
  -e POSTGRES_DB=upaffe -e POSTGRES_USER=upaffe \
  -e POSTGRES_PASSWORD="$password" postgres:18 >/dev/null

for attempt in $(seq 1 40); do
  if docker exec "$database" pg_isready -U upaffe -d upaffe >/dev/null 2>&1; then
    break
  fi
  if [ "$attempt" -eq 40 ]; then
    echo 'PostgreSQL did not become ready' >&2
    exit 1
  fi
  sleep 2
done

docker run -d --name "$application" --network "$network" \
  -e "ConnectionStrings__Postgres=Host=$database;Port=5432;Database=upaffe;Username=upaffe;Password=$password" \
  "$image" >/dev/null

for attempt in $(seq 1 60); do
  if docker exec "$application" curl --fail --silent \
    http://localhost:8080/api/health/ready >/dev/null; then
    break
  fi
  if [ "$attempt" -eq 60 ]; then
    echo 'Application did not become ready' >&2
    exit 1
  fi
  sleep 2
done

docker exec "$application" curl --fail --silent http://localhost:8080/ \
  | grep -q '<div id="root"></div>'
version="$(docker exec "$application" curl --fail --silent -D - -o /dev/null \
  http://localhost:8080/api/health/ready | tr -d '\r' | sed -n 's/^[Uu]paffe-[Vv]ersion: //p')"
[ -n "$version" ] || { echo 'Missing version response header' >&2; exit 1; }
[ "$(docker inspect -f '{{.Config.User}}' "$application")" != '0' ]
echo "Image ready, web assets served, version: $version"
