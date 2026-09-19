#!/bin/sh
# Run from the production Compose directory. The destination must not exist.
set -eu
umask 077

if [ "$#" -ne 2 ]; then
  echo 'Usage: ./backup-production.sh BACKUP_DIRECTORY COMPOSE_PROJECT' >&2
  exit 2
fi

backup=$1
project=$2
case "$project" in
  ''|*[!a-z0-9_-]*|[!a-z0-9]*)
    echo 'COMPOSE_PROJECT must start with a lowercase letter or digit and contain only lowercase letters, digits, hyphens, or underscores.' >&2
    exit 2 ;;
esac

if [ -e "$backup" ] || [ -L "$backup" ]; then
  echo 'Backup destination already exists.' >&2
  exit 1
fi
for required in .env docker-compose.yml docker-compose.verify-restore.yml \
  backup-production.sh restore-production.sh secrets/postgres_password; do
  if [ ! -f "$required" ]; then
    echo "Required deployment file is missing: $required" >&2
    exit 1
  fi
done

mkdir -m 0700 "$backup"
complete=false
cleanup() {
  if [ "$complete" != true ]; then
    rm -rf -- "$backup"
  fi
}
trap cleanup EXIT HUP INT TERM

cp -p .env docker-compose.yml docker-compose.verify-restore.yml \
  backup-production.sh restore-production.sh "$backup/"
mkdir -m 0700 "$backup/secrets"
cp -p secrets/postgres_password "$backup/secrets/"
for optional in docker-compose.bootstrap.yml docker-compose.heartbeat.yml; do
  if [ -f "$optional" ]; then
    cp -p "$optional" "$backup/"
  fi
done
for optional in bootstrap_proof heartbeat_url; do
  if [ -f "secrets/$optional" ]; then
    cp -p "secrets/$optional" "$backup/secrets/"
  fi
done

if ! docker compose -p "$project" -f docker-compose.yml exec -T db \
  pg_dump --username=upaffe --dbname=upaffe --format=custom \
  > "$backup/database.dump" 2> "$backup/backup-error.log"; then
  echo 'Database backup failed; no backup was retained.' >&2
  exit 1
fi
if [ ! -s "$backup/database.dump" ]; then
  echo 'Database backup was empty; no backup was retained.' >&2
  exit 1
fi
digest=$(cd "$backup" && openssl dgst -sha256 -r database.dump)
printf '%s\n' "${digest%% *}" > "$backup/database.sha256"
rm "$backup/backup-error.log"
complete=true
trap - EXIT HUP INT TERM
echo 'Backup completed in the protected destination directory.'
