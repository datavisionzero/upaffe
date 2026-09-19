# Release publication

The [release workflow](../.github/workflows/release.yml) is the only path that publishes a versioned image and GitHub release. It accepts a nonpublishing candidate dispatch from current `main` or a stable `vX.Y.Z` tag on a validated `main` commit. A tag is the maintainer's publication action; dispatching a candidate creates no tag, versioned registry image, or GitHub release.

## Candidate review

1. Wait for the `main` [CI workflow](../.github/workflows/ci.yml) to pass and publish its `sha-<full-commit-sha>` image. Confirm its source revision and both Linux architectures.
2. Review `docs/releases/vX.Y.Z.md` in the proposed commit. The release body is built from this checked-in file, the exact source revision, and the image index digest; it is not generated from commit messages.
3. Dispatch **Release candidate and publication** on `main` with a prerelease version such as `0.1.0-rc.1`. Its preflight requires a passing `main` CI run for that commit. The workflow builds and executes four CLI archives on native runners, builds a two-platform OCI image archive without pushing it, checks both images' version and revision labels, and uploads `release-candidate` with `SHA256SUMS`, `source-revision.txt`, notes preview, and candidate OCI layout hash.
4. Verify the candidate bundle checksums and source revision, then run the [MVP acceptance gate](./release-acceptance.md) against the same commit. Resolve any failure before creating the stable tag.

## Tag and published assets

Create and push a stable tag, for example `v0.1.0`, on the accepted `main` commit. The workflow repeats candidate validation before its publication job receives `contents: write` and `packages: write` permissions. It publishes `ghcr.io/datavisionzero/upaffe:vX.Y.Z` with Linux amd64 and arm64 and keeps the existing `sha-<full-commit-sha>` image path. The application response header and OCI labels report the release version; OCI revision labels report the source commit.

The GitHub release contains `ua_X.Y.Z_linux_amd64.zip`, `ua_X.Y.Z_linux_arm64.zip`, `ua_X.Y.Z_darwin_amd64.zip`, and `ua_X.Y.Z_darwin_arm64.zip`; `SHA256SUMS`; `source-revision.txt`; and `image-digest.txt`. Each CLI archive contains only executable `ua`. The release body lists the immutable image digest and links to the [installation guide](./operations.md) and [CLI guide](./cli.md). Verify the archive checksum before installing it. Pin the image digest in production.

If a run stops after creating a draft release, rerun the same workflow for that tag. It reuses a versioned image only when both architecture manifests and their version/revision labels match. It accepts existing release assets only when their GitHub SHA-256 digest equals the locally checked asset, uploads missing assets, and publishes the draft only after every expected asset is present. A different image, notes body, or existing asset causes the run to fail for maintainer investigation. An already published release is verified and left intact.
