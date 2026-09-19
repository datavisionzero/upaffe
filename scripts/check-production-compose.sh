#!/usr/bin/env bash
set -euo pipefail

export UPAFFE_IMAGE="${UPAFFE_IMAGE:-ghcr.io/datavisionzero/upaffe:sha-validation-only}"
base_config=$(mktemp)
bootstrap_config=$(mktemp)
trap 'rm -f "$base_config" "$bootstrap_config"' EXIT

docker compose -f deploy/docker-compose.yml config --format json > "$base_config"
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.bootstrap.yml \
  config --format json > "$bootstrap_config"

python3 - "$base_config" "$bootstrap_config" <<'PY'
import json
import os
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    base = json.load(source)
with open(sys.argv[2], encoding="utf-8") as source:
    bootstrap = json.load(source)

app = base["services"]["app"]
database = base["services"]["db"]
assert app["image"] == os.environ["UPAFFE_IMAGE"]
assert "build" not in app
assert not database.get("ports")
assert base["networks"]["database"]["internal"] is True
assert set(database["networks"]) == {"database"}
assert set(app["networks"]) == {"database", "edge"}
assert len(app["ports"]) == 1 and app["ports"][0]["host_ip"] == "127.0.0.1"
assert database["environment"]["POSTGRES_PASSWORD_FILE"] == "/run/secrets/postgres_password"
assert app["environment"]["UPAFFE_POSTGRES_PASSWORD_FILE"] == "/run/secrets/postgres_password"
for service in (app, database):
    environment = service["environment"]
    assert "ConnectionStrings__Postgres" not in environment
    assert "POSTGRES_PASSWORD" not in environment
    assert "UPAFFE_BOOTSTRAP_SECRET" not in environment
assert "UPAFFE_BOOTSTRAP_SECRET_FILE" not in app["environment"]
assert "postgres-data" in base["volumes"]
assert database.get("depends_on") is None
assert app["depends_on"]["db"]["condition"] == "service_healthy"
assert "health/ready" in " ".join(app["healthcheck"]["test"])

bootstrap_app = bootstrap["services"]["app"]
assert bootstrap_app["environment"]["UPAFFE_BOOTSTRAP_SECRET_FILE"] == "/run/secrets/bootstrap_proof"
assert {secret["source"] for secret in bootstrap_app["secrets"]} == {
    "postgres_password", "bootstrap_proof"
}
assert "bootstrap_proof" not in base["secrets"]
PY
