#!/bin/sh
# Restore a backup into a new Compose directory and unused project/volume.
set -eu
umask 077

if [ "$#" -ne 3 ]; then
  echo 'Usage: ./restore-production.sh BACKUP_DIRECTORY NEW_DEPLOY_DIRECTORY COMPOSE_PROJECT' >&2
  exit 2
fi

backup=$1
destination=$2
project=$3
case "$project" in
  ''|*[!a-z0-9_-]*|[!a-z0-9]*)
    echo 'COMPOSE_PROJECT must start with a lowercase letter or digit and contain only lowercase letters, digits, hyphens, or underscores.' >&2
    exit 2 ;;
esac

if [ -e "$destination" ] || [ -L "$destination" ]; then
  echo 'Restore destination already exists.' >&2
  exit 1
fi
for required in .env docker-compose.yml docker-compose.verify-restore.yml \
  backup-production.sh restore-production.sh database.dump database.sha256 \
  secrets/postgres_password; do
  if [ ! -f "$backup/$required" ]; then
    echo "Backup is incomplete: $required is missing." >&2
    exit 1
  fi
done
expected=$(cat "$backup/database.sha256")
digest=$(cd "$backup" && openssl dgst -sha256 -r database.dump)
actual=${digest%% *}
if [ -z "$actual" ] || [ "$actual" != "$expected" ]; then
  echo 'Database archive checksum does not match.' >&2
  exit 1
fi
if docker volume inspect "${project}_postgres-data" >/dev/null 2>&1 \
  || docker network inspect "${project}_database" >/dev/null 2>&1 \
  || docker network inspect "${project}_edge" >/dev/null 2>&1 \
  || [ -n "$(docker ps -aq --filter "label=com.docker.compose.project=$project")" ]; then
  echo 'Compose project already has Docker resources; choose a new project.' >&2
  exit 1
fi

mkdir -m 0700 "$destination"
cp -p "$backup/.env" "$backup/docker-compose.yml" \
  "$backup/docker-compose.verify-restore.yml" \
  "$backup/backup-production.sh" "$backup/restore-production.sh" \
  "$backup/database.dump" "$backup/database.sha256" "$destination/"
mkdir -m 0700 "$destination/secrets"
cp -p "$backup/secrets/postgres_password" "$destination/secrets/"
for optional in docker-compose.bootstrap.yml docker-compose.heartbeat.yml; do
  if [ -f "$backup/$optional" ]; then
    cp -p "$backup/$optional" "$destination/"
  fi
done
for optional in bootstrap_proof heartbeat_url; do
  if [ -f "$backup/secrets/$optional" ]; then
    cp -p "$backup/secrets/$optional" "$destination/secrets/"
  fi
done
chmod 0644 "$destination/secrets/"*

cd "$destination"
if ! docker compose -p "$project" -f docker-compose.yml up -d --wait db; then
  echo 'Database startup failed; the isolated restore directory was retained.' >&2
  exit 1
fi
if ! docker compose -p "$project" -f docker-compose.yml exec -T db \
  pg_restore --list < database.dump > /dev/null 2> restore-error.log; then
  echo 'Database archive could not be read; the isolated restore was retained.' >&2
  exit 1
fi
if ! docker compose -p "$project" -f docker-compose.yml exec -T db \
  pg_restore --username=upaffe --dbname=upaffe --single-transaction \
  --no-owner --no-acl < database.dump 2> restore-error.log; then
  echo 'Database restore failed; the isolated restore was retained.' >&2
  exit 1
fi
rm restore-error.log
echo 'Database restored into the new Compose project; verify before starting the application.'
