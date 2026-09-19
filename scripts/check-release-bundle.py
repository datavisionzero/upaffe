#!/usr/bin/env python3
"""Check the exact CLI asset set used by a release candidate or publication."""

import hashlib
import pathlib
import re
import sys


PLATFORMS = ("linux_amd64", "linux_arm64", "darwin_amd64", "darwin_arm64")


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit("Usage: check-release-bundle.py VERSION REVISION DIRECTORY")
    version, revision, directory = sys.argv[1:]
    root = pathlib.Path(directory)
    archives = {f"ua_{version}_{platform}.zip" for platform in PLATFORMS}
    expected_files = archives | {"SHA256SUMS", "source-revision.txt"}
    actual_files = {path.name for path in root.iterdir() if path.is_file()}
    if actual_files != expected_files:
        raise SystemExit(f"CLI bundle file set differs: {actual_files ^ expected_files}")
    if (root / "source-revision.txt").read_text(encoding="ascii") != f"{revision}\n":
        raise SystemExit("CLI bundle source revision differs")
    entries = (root / "SHA256SUMS").read_text(encoding="ascii").splitlines()
    if len(entries) != 4:
        raise SystemExit("CLI checksum manifest must have four entries")
    seen: set[str] = set()
    for entry in entries:
        match = re.fullmatch(r"([0-9a-f]{64})  (ua_[A-Za-z0-9._-]+\.zip)", entry)
        if match is None or match.group(2) not in archives or match.group(2) in seen:
            raise SystemExit("Invalid CLI checksum manifest entry")
        digest, name = match.groups()
        if hashlib.sha256((root / name).read_bytes()).hexdigest() != digest:
            raise SystemExit(f"CLI archive checksum mismatch: {name}")
        seen.add(name)
    if seen != archives:
        raise SystemExit("CLI checksum manifest is incomplete")
    print(f"Four CLI release archives verified for {version} at {revision}.")


if __name__ == "__main__":
    main()
