#!/usr/bin/env python3
"""Verify both executable OCI image manifests in a candidate archive."""

import json
import sys
import tarfile


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit("Usage: check-release-image-layout.py OCI_TAR VERSION REVISION")
    archive, version, revision = sys.argv[1:]
    with tarfile.open(archive) as image:
        def read_json(name: str) -> dict:
            member = image.extractfile(name)
            if member is None:
                raise SystemExit(f"Missing OCI entry: {name}")
            return json.load(member)

        def blob(descriptor: dict) -> dict:
            digest = descriptor["digest"]
            if not digest.startswith("sha256:"):
                raise SystemExit("Unexpected OCI digest algorithm")
            return read_json(f"blobs/sha256/{digest.removeprefix('sha256:')}")

        expected = {("linux", "amd64"), ("linux", "arm64")}
        found: set[tuple[str, str]] = set()

        def visit(index: dict) -> None:
            for descriptor in index["manifests"]:
                if descriptor["mediaType"] == "application/vnd.oci.image.index.v1+json":
                    visit(blob(descriptor))
                    continue
                platform = descriptor.get("platform", {})
                key = (platform.get("os"), platform.get("architecture"))
                if key[0] != "linux":
                    continue  # BuildKit provenance has no executable platform.
                if key not in expected or key in found:
                    raise SystemExit(f"Unexpected or duplicate OCI platform: {key}")
                manifest = blob(descriptor)
                config = blob(manifest["config"])
                labels = config.get("config", {}).get("Labels", {})
                if labels.get("org.opencontainers.image.version") != version:
                    raise SystemExit(f"Wrong version label for {key}")
                if labels.get("org.opencontainers.image.revision") != revision:
                    raise SystemExit(f"Wrong revision label for {key}")
                if (config.get("os"), config.get("architecture")) != key:
                    raise SystemExit(f"Wrong image configuration platform for {key}")
                found.add(key)

        visit(read_json("index.json"))
    if found != expected:
        raise SystemExit(f"Incomplete OCI platform set: {found}")
    print(f"Candidate image contains Linux amd64 and arm64 for {version} at {revision}.")


if __name__ == "__main__":
    main()
