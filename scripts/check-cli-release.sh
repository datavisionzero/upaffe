#!/bin/sh
# Verify the complete archive format and execute the binary on its native host.
set -eu

if [ "$#" -ne 3 ]; then
  echo 'Usage: scripts/check-cli-release.sh VERSION PLATFORM OUTPUT_DIR' >&2
  exit 2
fi
version=$1
platform=$2
output_dir=$3
case "$(uname -s):$(uname -m)" in
  Linux:x86_64) native=linux_amd64 ;;
  Linux:aarch64|Linux:arm64) native=linux_arm64 ;;
  Darwin:x86_64) native=darwin_amd64 ;;
  Darwin:arm64) native=darwin_arm64 ;;
  *) echo 'Unsupported native release runner.' >&2; exit 2 ;;
esac
if [ "$platform" != "$native" ]; then
  echo 'Archive platform does not match the native runner.' >&2
  exit 1
fi

root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
revision=$(git -C "$root" rev-parse HEAD)
python3 - "$version" "$platform" "$output_dir" "$revision" <<'PY'
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import zipfile

version, platform, directory, revision = sys.argv[1:]
directory = pathlib.Path(directory)
archive = directory / f"ua_{version}_{platform}.zip"
checksum = archive.with_suffix(".zip.sha256")
expected = checksum.read_text(encoding="ascii")
actual = hashlib.sha256(archive.read_bytes()).hexdigest()
if expected != f"{actual}  {archive.name}\n":
    raise SystemExit("CLI archive checksum mismatch")

with tempfile.TemporaryDirectory(prefix="upaffe-cli-check-") as work:
    binary = pathlib.Path(work) / "ua"
    with zipfile.ZipFile(archive) as zipped:
        if zipped.namelist() != ["ua"]:
            raise SystemExit("CLI archive must contain only ua")
        info = zipped.getinfo("ua")
        mode = info.external_attr >> 16
        if mode & 0o111 == 0:
            raise SystemExit("CLI binary is not executable")
        binary.write_bytes(zipped.read("ua"))
        binary.chmod(0o755)

    metadata = subprocess.run(["go", "version", "-m", str(binary)],
                              text=True, capture_output=True, check=True).stdout
    if f"vcs.revision={revision}" not in metadata:
        raise SystemExit("CLI binary source revision mismatch")
    result = subprocess.run([str(binary), "version", "--json"],
                            text=True, capture_output=True, check=True)
    if json.loads(result.stdout).get("version") != version or result.stderr:
        raise SystemExit("CLI version output mismatch")
    help_result = subprocess.run([str(binary), "--help"],
                                 text=True, capture_output=True, check=True)
    if "Usage:" not in help_result.stdout or help_result.stderr:
        raise SystemExit("CLI help failed")
    unavailable = subprocess.run(
        [str(binary), "status", "--url", "http://127.0.0.1:1", "--json"],
        text=True, capture_output=True, timeout=15)
    if (unavailable.returncode != 10 or unavailable.stdout or
            json.loads(unavailable.stderr).get("code") != "instance_unreachable"):
        raise SystemExit("CLI process failure contract changed")
print(f"Native {platform} CLI archive verified: {archive.name}")
PY
