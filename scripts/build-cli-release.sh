#!/bin/sh
# Build one versioned CLI archive from an exact, clean source revision.
set -eu

if [ "$#" -ne 3 ]; then
  echo 'Usage: scripts/build-cli-release.sh VERSION linux_amd64|linux_arm64|darwin_amd64|darwin_arm64 OUTPUT_DIR' >&2
  exit 2
fi
version=$1
platform=$2
output_dir=$3
if ! printf '%s\n' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'; then
  echo 'VERSION must be a numeric release or prerelease version.' >&2
  exit 2
fi
case "$platform" in
  linux_amd64|linux_arm64|darwin_amd64|darwin_arm64) ;;
  *) echo 'Unsupported CLI release platform.' >&2; exit 2 ;;
esac

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
if [ -n "$(git -C "$root" status --porcelain --untracked-files=all)" ]; then
  echo 'Release archives require a clean source revision.' >&2
  exit 1
fi
if [ "$(go env GOVERSION)" != 'go1.27.1' ]; then
  echo 'Release archives require the Go version pinned in src/cli/go.mod.' >&2
  exit 1
fi

source_revision=$(git -C "$root" rev-parse HEAD)
if [ -n "${UPAFFE_RELEASE_SOURCE_SHA:-}" ] && [ "$source_revision" != "$UPAFFE_RELEASE_SOURCE_SHA" ]; then
  echo 'Checked-out source does not match the requested revision.' >&2
  exit 1
fi

mkdir -p "$output_dir"
output_dir=$(CDPATH='' cd -- "$output_dir" && pwd)
work=$(mktemp -d "${TMPDIR:-/tmp}/upaffe-cli-release.XXXXXX")
trap 'rm -rf -- "$work"' EXIT

cd "$root/src/cli"
go mod verify
go generate ./...
git -C "$root" diff --exit-code -- src/cli/internal/api/client.gen.go

target_os=${platform%_*}
target_arch=${platform#*_}
CGO_ENABLED=0 GOOS="$target_os" GOARCH="$target_arch" go build \
  -trimpath -buildvcs=true \
  -ldflags "-s -w -X github.com/datavisionzero/upaffe/src/cli/internal/version.Value=$version" \
  -o "$work/ua" ./cmd/ua

archive_name="ua_${version}_${platform}.zip"
python3 - "$work/ua" "$output_dir/$archive_name" "$source_revision" <<'PY'
import hashlib
import pathlib
import sys
import zipfile

binary = pathlib.Path(sys.argv[1])
archive = pathlib.Path(sys.argv[2])
revision = sys.argv[3]
info = zipfile.ZipInfo("ua", date_time=(1980, 1, 1, 0, 0, 0))
info.compress_type = zipfile.ZIP_DEFLATED
info.create_system = 3
info.external_attr = 0o100755 << 16
with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as output:
    output.writestr(info, binary.read_bytes())
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix(archive.suffix + ".sha256").write_text(
    f"{digest}  {archive.name}\n", encoding="ascii")
print(f"{archive.name} {revision} {digest}")
PY
