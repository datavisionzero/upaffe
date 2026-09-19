#!/bin/sh
# Inspect the published index and both architecture-specific image labels.
set -eu
if [ "$#" -ne 3 ]; then
  echo 'Usage: scripts/check-release-image.sh IMAGE VERSION REVISION' >&2
  exit 2
fi
image=$1
version=$2
revision=$3

docker buildx imagetools inspect --raw "$image" \
  | jq -e '[.manifests[] | select(.platform.os == "linux") |
      .platform.architecture] | sort == ["amd64", "arm64"]' >/dev/null || {
    echo 'Release image index lacks exactly Linux amd64 and arm64.' >&2; exit 1;
  }

for architecture in amd64 arm64; do
  docker pull --quiet --platform "linux/$architecture" "$image" >/dev/null
  actual=$(docker image inspect --platform "linux/$architecture" "$image" --format \
    '{{.Os}}/{{.Architecture}} {{index .Config.Labels "org.opencontainers.image.version"}} {{index .Config.Labels "org.opencontainers.image.revision"}}')
  [ "$actual" = "linux/$architecture $version $revision" ] || {
    echo "Wrong metadata for the $architecture release image." >&2; exit 1;
  }
done

case "$(docker info --format '{{.Architecture}}')" in
  x86_64|amd64) native=amd64 ;;
  aarch64|arm64) native=arm64 ;;
  *) echo 'Unsupported native Docker architecture.' >&2; exit 1 ;;
esac
docker pull --quiet --platform "linux/$native" "$image" >/dev/null
root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
"$root/scripts/check-image.sh" "$image" | grep -F "version: $version" >/dev/null || {
  echo 'Release image did not report its embedded version.' >&2; exit 1;
}

digest=$(docker buildx imagetools inspect "$image" | sed -n 's/^Digest:[[:space:]]*//p' | head -1)
case "$digest" in
  sha256:????????????????????????????????????????????????????????????????) ;;
  *) echo 'Release image has no SHA-256 index digest.' >&2; exit 1 ;;
esac
printf '%s\n' "$digest"
