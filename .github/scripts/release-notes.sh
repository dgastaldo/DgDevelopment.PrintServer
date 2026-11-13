#!/usr/bin/env bash
set -euo pipefail

TAG="${1:?usage: release-notes.sh <tag> <environment>}"
ENVIRONMENT="${2:?usage: release-notes.sh <tag> <environment>}"

VERSION="${TAG#*/}"
VERSION="${VERSION#v}"
VERSION="${VERSION%%+*}"

echo "Server ${VERSION} in the ${ENVIRONMENT} environment (tag \`${TAG}\`)."
echo

SECTION="$(awk -v v="${VERSION}" '
  $0 ~ "^## \\[?" v "[]( ]" { found = 1; next }
  /^## / { found = 0 }
  found { print }
' CHANGELOG.md 2>/dev/null || true)"

if [[ -n "${SECTION}" ]]; then
  echo "${SECTION}"
else
  echo "See [CHANGELOG.md](CHANGELOG.md) for the changes in this version."
fi
