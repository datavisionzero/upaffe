#!/usr/bin/env python3
"""Safely create or resume one fully checked GitHub release for an existing tag."""

from __future__ import annotations

import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

PLATFORMS = ("linux_amd64", "linux_arm64", "darwin_amd64", "darwin_arm64")


def gh(*arguments: str) -> str:
    result = subprocess.run(["gh", *arguments], text=True, capture_output=True)
    if result.returncode:
        raise SystemExit(result.stderr.strip() or f"gh {' '.join(arguments)} failed")
    return result.stdout


def release(repo: str, tag: str) -> dict | None:
    releases = json.loads(gh("api", f"repos/{repo}/releases?per_page=100"))
    matches = [item for item in releases if item["tag_name"] == tag]
    if len(matches) > 1:
        raise SystemExit("Multiple releases have the same tag")
    return matches[0] if matches else None


def verify_assets(existing: dict, paths: dict[str, pathlib.Path], allow_missing: bool) -> set[str]:
    assets = {item["name"]: item for item in existing["assets"]}
    if len(assets) != len(existing["assets"]) or assets.keys() - paths.keys():
        raise SystemExit("Release contains unexpected or duplicate assets")
    for name, item in assets.items():
        expected = f"sha256:{hashlib.sha256(paths[name].read_bytes()).hexdigest()}"
        if item.get("digest") != expected or item.get("state") != "uploaded":
            raise SystemExit(f"Existing release asset differs: {name}")
    if not allow_missing and assets.keys() != paths.keys():
        raise SystemExit("Published release has missing assets")
    return set(assets)


def main() -> None:
    if len(sys.argv) != 6:
        raise SystemExit("Usage: publish-release.py VERSION REVISION IMAGE_DIGEST BUNDLE_DIR NOTES_FILE")
    version, revision, image_digest, bundle_arg, notes_arg = sys.argv[1:]
    tag = f"v{version}"
    repo = os.environ["GITHUB_REPOSITORY"]
    if repo != "datavisionzero/upaffe" or re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version) is None:
        raise SystemExit("Unexpected repository or public version")
    if len(revision) != 40 or any(c not in "0123456789abcdef" for c in revision):
        raise SystemExit("Invalid source revision")
    if re.fullmatch(r"sha256:[0-9a-f]{64}", image_digest) is None:
        raise SystemExit("Invalid image index digest")

    bundle = pathlib.Path(bundle_arg)
    subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name("check-release-bundle.py")),
                    version, revision, str(bundle)], check=True)
    image_ref = f"ghcr.io/{repo}:{tag}"
    canonical_image = f"ghcr.io/{repo}@{image_digest}"
    reviewed = pathlib.Path(notes_arg).read_text(encoding="utf-8").rstrip()
    body = (f"{reviewed}\n\n## Verified artifacts\n\n"
            f"- Source revision: `{revision}`\n"
            f"- Image: `{image_ref}`; immutable digest: `{canonical_image}`\n"
            f"- CLI archives: four platform files listed in `SHA256SUMS`\n"
            f"- Verify downloads with `SHA256SUMS` before execution.\n")
    digest_file = bundle / "image-digest.txt"
    digest_file.write_text(f"{canonical_image}\n", encoding="ascii")
    names = [f"ua_{version}_{platform}.zip" for platform in PLATFORMS]
    names += ["SHA256SUMS", "source-revision.txt", "image-digest.txt"]
    paths = {name: bundle / name for name in names}

    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", suffix=".md") as notes:
        notes.write(body)
        notes.flush()
        current = release(repo, tag)
        if current is None:
            gh("release", "create", tag, "--draft", "--verify-tag", "--title",
               f"upaffe {tag}", "--notes-file", notes.name)
            current = release(repo, tag)
            if current is None:
                raise SystemExit("Draft release was not found after creation")
        if current["body"].rstrip() != body.rstrip():
            raise SystemExit("Release notes differ from the reviewed source and digest")
        uploaded = verify_assets(current, paths, allow_missing=current["draft"])
        if not current["draft"]:
            print(f"Previously published {tag} matches every expected asset.")
            return
        for name in names:
            if name in uploaded:
                continue
            gh("release", "upload", tag, str(paths[name]))
            current = release(repo, tag)
            if current is None:
                raise SystemExit("Draft release vanished during upload")
            uploaded = verify_assets(current, paths, allow_missing=True)
        verify_assets(current, paths, allow_missing=False)
        gh("release", "edit", tag, "--draft=false")
        published = release(repo, tag)
        if published is None or published["draft"]:
            raise SystemExit("Release did not become public")
        verify_assets(published, paths, allow_missing=False)
    print(f"Published {tag} with four CLI archives and image {image_digest}.")


if __name__ == "__main__":
    main()
