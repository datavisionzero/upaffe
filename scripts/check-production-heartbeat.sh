#!/bin/sh
# Exercise the optional heartbeat against a disposable, locally trusted HTTPS receiver.
set -eu
umask 077

if [ "$#" -ne 1 ]; then
  echo 'Usage: scripts/check-production-heartbeat.sh PUBLISHED_IMAGE' >&2
  exit 2
fi
image=$1
case "$image" in
  ghcr.io/datavisionzero/upaffe:sha-*) ;;
  *) echo 'Expected a published full-revision GHCR image tag.' >&2; exit 2 ;;
esac
root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
mkdir -p "$root/scratchpad"
work=$(mktemp -d "$root/scratchpad/upaffe-heartbeat.XXXXXX")
project="upaffe-heartbeat-$(openssl rand -hex 6)"
port=${UPAFFE_HEARTBEAT_TEST_PORT:-18087}
compose='docker-compose.yml'
cleanup() {
  (cd "$work" && docker compose -p "$project" -f docker-compose.yml \
    -f docker-compose.heartbeat.yml -f receiver-overlay.yml down --volumes) >/dev/null 2>&1 || true
  rm -rf -- "$work"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM

cp "$root/deploy/docker-compose.yml" "$root/deploy/docker-compose.heartbeat.yml" "$work/"
printf 'UPAFFE_IMAGE=%s\nUPAFFE_PORT=%s\nUPAFFE_POSTGRES_IMAGE=postgres:18\n' \
  "$image" "$port" > "$work/.env"
mkdir -m 0700 "$work/secrets"
openssl rand -hex 24 > "$work/secrets/postgres_password"
receiver_token=$(openssl rand -hex 24)
printf 'https://%s/ping/%s\n' receiver.example.test "$receiver_token" > "$work/secrets/heartbeat_url"
chmod 0644 "$work/secrets/"*
openssl req -x509 -newkey rsa:2048 -nodes -days 1 \
  -keyout "$work/receiver.key" -out "$work/receiver.crt" \
  -subj '/CN=receiver.example.test' \
  -addext 'subjectAltName=DNS:receiver.example.test' >/dev/null 2>&1
chmod 0644 "$work/receiver.crt" "$work/receiver.key"
cat > "$work/receiver.conf" <<'CONF'
server {
    listen 443 ssl;
    server_name receiver.example.test;
    ssl_certificate /etc/nginx/receiver.crt;
    ssl_certificate_key /etc/nginx/receiver.key;
    access_log /dev/stdout combined;
    location /ping/ { return 204; }
}
CONF
cat > "$work/receiver-overlay.yml" <<'OVERLAY'
services:
  receiver:
    image: nginx:1.29-alpine
    volumes:
      - ./receiver.conf:/etc/nginx/conf.d/default.conf:ro
      - ./receiver.crt:/etc/nginx/receiver.crt:ro
      - ./receiver.key:/etc/nginx/receiver.key:ro
    networks:
      edge:
        aliases:
          - receiver.example.test
  app:
    environment:
      SSL_CERT_FILE: /run/receiver-ca.pem
    volumes:
      - ./receiver.crt:/run/receiver-ca.pem:ro
OVERLAY
cat > "$work/monitoring-off.yml" <<'OVERLAY'
services:
  app:
    environment:
      Monitoring__Enabled: "false"
OVERLAY
cd "$work"
base_url="http://127.0.0.1:$port"
count_requests() {
  docker compose -p "$project" -f "$compose" -f receiver-overlay.yml \
    logs --no-log-prefix receiver 2>/dev/null | grep -c 'GET /ping/' || true
}
wait_for_progress() {
  tries=0
  while [ "$tries" -lt 30 ]; do
    if curl --fail --silent "$base_url/api/health/progress" | grep -q '"status":"progressing"'; then
      return 0
    fi
    sleep 1
    tries=$((tries + 1))
  done
  echo 'Monitoring progress did not become healthy.' >&2
  exit 1
}
wait_for_requests() {
  baseline=$1
  tries=0
  while [ "$tries" -lt 80 ]; do
    count=$(count_requests)
    if [ "$count" -gt "$baseline" ]; then return 0; fi
    sleep 1
    tries=$((tries + 1))
  done
  echo 'No healthy heartbeat reached the receiver.' >&2
  exit 1
}

echo 'Heartbeat: base stack has no sender.'
docker compose -p "$project" -f "$compose" -f receiver-overlay.yml up -d --wait >/dev/null
wait_for_progress
[ "$(count_requests)" = 0 ]

echo 'Heartbeat: enabled sender reaches the HTTPS receiver.'
docker compose -p "$project" -f "$compose" -f docker-compose.heartbeat.yml \
  -f receiver-overlay.yml up -d --wait --force-recreate app >/dev/null
wait_for_requests 0

echo 'Heartbeat: stopped monitoring leaves HTTP live but progress stalled.'
docker compose -p "$project" -f "$compose" -f docker-compose.heartbeat.yml \
  -f receiver-overlay.yml -f monitoring-off.yml up -d --wait --force-recreate app >/dev/null
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$base_url/api/health/live")" = 200 ]
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$base_url/api/health/ready")" = 200 ]
[ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$base_url/api/health/progress")" = 503 ]
stalled_count=$(count_requests)
sleep 66
[ "$(count_requests)" = "$stalled_count" ]

echo 'Heartbeat: monitoring recovery restores progress and outbound requests.'
docker compose -p "$project" -f "$compose" -f docker-compose.heartbeat.yml \
  -f receiver-overlay.yml up -d --wait --force-recreate app >/dev/null
wait_for_progress
wait_for_requests "$stalled_count"
if docker compose -p "$project" -f "$compose" -f receiver-overlay.yml \
  logs app 2>/dev/null | grep -Fq "$receiver_token"; then
  echo 'Receiver URL appeared in application logs.' >&2
  exit 1
fi
echo 'Heartbeat, stalled progress, no send while stalled, and recovery passed.'
