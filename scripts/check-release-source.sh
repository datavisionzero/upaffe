#!/bin/sh
# A release must come from a validated main commit and an existing revision image.
set -eu

requested_version=${1:-}
case "${GITHUB_EVENT_NAME:-}" in
  workflow_dispatch)
    [ "${GITHUB_REF:-}" = 'refs/heads/main' ] || {
      echo 'Candidate runs must use main.' >&2; exit 1;
    }
    version=$requested_version
    ;;
  push)
    case "${GITHUB_REF:-}" in
      refs/tags/v*) version=${GITHUB_REF#refs/tags/v} ;;
      *) echo 'Release publication requires a version tag.' >&2; exit 1 ;;
    esac
    [ -z "$requested_version" ] || {
      echo 'Tag runs cannot override their version.' >&2; exit 1;
    }
    ;;
  *) echo 'Unsupported release event.' >&2; exit 1 ;;
esac

if ! printf '%s\n' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'; then
  echo 'Invalid release version.' >&2
  exit 1
fi
if [ "$GITHUB_EVENT_NAME" = push ] && ! printf '%s\n' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo 'Public release tags must be stable versions.' >&2
  exit 1
fi

source_revision=$(git rev-parse HEAD)
[ "$source_revision" = "${GITHUB_SHA:-}" ] || {
  echo 'Checked-out commit differs from the event commit.' >&2; exit 1;
}
git fetch --quiet origin main
git merge-base --is-ancestor "$source_revision" origin/main || {
  echo 'Release source is not on main.' >&2; exit 1;
}
if [ "$GITHUB_EVENT_NAME" = workflow_dispatch ]; then
  [ "$source_revision" = "$(git rev-parse origin/main)" ] || {
    echo 'Candidate runs require the current main revision.' >&2; exit 1;
  }
else
  [ "$(git rev-list -n 1 "$GITHUB_REF")" = "$source_revision" ] || {
    echo 'Version tag does not resolve to the checked-out commit.' >&2; exit 1;
  }
fi

notes="docs/releases/v${version%%-*}.md"
[ -s "$notes" ] || { echo 'Reviewed release notes are missing.' >&2; exit 1; }

gh api "repos/$GITHUB_REPOSITORY/actions/workflows/ci.yml/runs?head_sha=$source_revision&branch=main&event=push&status=success&per_page=100" \
  | jq -e --arg sha "$source_revision" '
      [.workflow_runs[] | select(.head_sha == $sha and .head_branch == "main" and
        .event == "push" and .conclusion == "success")] | length > 0' >/dev/null || {
    echo 'No passing main CI run exists for this commit.' >&2; exit 1;
  }

revision_image="ghcr.io/datavisionzero/upaffe:sha-$source_revision"
docker buildx imagetools inspect --raw "$revision_image" \
  | jq -e '[.manifests[] | select(.platform.os == "linux") |
      .platform.architecture] | sort == ["amd64", "arm64"]' >/dev/null || {
    echo 'Validated revision image lacks its two Linux architectures.' >&2; exit 1;
  }

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  printf 'version=%s\nsource_revision=%s\nnotes=%s\n' \
    "$version" "$source_revision" "$notes" >> "$GITHUB_OUTPUT"
fi
printf 'Validated %s from %s with a passing main CI run and revision image.\n' \
  "$version" "$source_revision"
