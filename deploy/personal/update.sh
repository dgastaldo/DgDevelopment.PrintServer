#!/usr/bin/env bash
set -euo pipefail

REPO="${REPO:-dgastaldo/DgDevelopment.PrintServer}"
API_URL="${API_URL:-https://api.github.com/repos/${REPO}/releases}"
INSTALL_DIR="${INSTALL_DIR:-/opt/printserver}"
SERVICE_NAME="${SERVICE_NAME:-printserver}"
LAUNCHD_LABEL="${LAUNCHD_LABEL:-com.printserver}"
HEALTH_URL="${HEALTH_URL:-}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-30}"
TAG_PREFIX="${TAG_PREFIX:-personal/v}"

CURRENT_DIR="${INSTALL_DIR}/current"
PREVIOUS_DIR="${INSTALL_DIR}/previous"
VERSION_FILE="${INSTALL_DIR}/.version"
PREVIOUS_VERSION_FILE="${INSTALL_DIR}/.version.previous"
LOCK_DIR="${INSTALL_DIR}/.update.lock"
FAILED_FILE="${INSTALL_DIR}/.failed-tag"
COMMAND="${1:-}"

log() { printf '%s %s\n' "$(date -u +%FT%TZ)" "$*"; }

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "${os}/${arch}" in
    Linux/x86_64) echo linux-x64 ;;
    Linux/aarch64 | Linux/arm64) echo linux-arm64 ;;
    Darwin/arm64) echo osx-arm64 ;;
    Darwin/x86_64) echo osx-x64 ;;
    *) log "unsupported platform ${os}/${arch}"; exit 1 ;;
  esac
}

restart_service() {
  if [[ "$(uname -s)" = "Darwin" ]]; then
    if launchctl print "system/${LAUNCHD_LABEL}" >/dev/null 2>&1; then
      launchctl kickstart -k "system/${LAUNCHD_LABEL}"
    else
      log "launchd service ${LAUNCHD_LABEL} not found, restart it manually"
    fi
  elif command -v systemctl >/dev/null 2>&1 && systemctl cat "${SERVICE_NAME}" >/dev/null 2>&1; then
    systemctl restart "${SERVICE_NAME}"
  else
    log "service ${SERVICE_NAME} not found, restart it manually"
  fi
}

healthy() {
  [[ -z "${HEALTH_URL}" ]] && return 0
  for _ in $(seq 1 "${HEALTH_TIMEOUT}"); do
    if curl -fsS -o /dev/null "${HEALTH_URL}"; then return 0; fi
    sleep 1
  done
  return 1
}

rollback() {
  if [[ ! -d "${PREVIOUS_DIR}" ]]; then
    log "no previous version to roll back to"
    exit 1
  fi
  local failed="${INSTALL_DIR}/failed"
  rm -rf "${failed}"
  mv "${CURRENT_DIR}" "${failed}"
  mv "${PREVIOUS_DIR}" "${CURRENT_DIR}"
  if [[ -f "${PREVIOUS_VERSION_FILE}" ]]; then mv "${PREVIOUS_VERSION_FILE}" "${VERSION_FILE}"; fi
  rm -rf "${failed}"
  restart_service
  log "rolled back to $(cat "${VERSION_FILE}" 2>/dev/null || echo unknown)"
}

mkdir -p "${INSTALL_DIR}"
if ! mkdir "${LOCK_DIR}" 2>/dev/null; then
  log "another update is running"
  exit 0
fi
WORK=""
cleanup() {
  rm -rf "${LOCK_DIR}"
  if [[ -n "${WORK}" ]]; then rm -rf "${WORK}"; fi
}
trap cleanup EXIT

if [[ "${COMMAND}" = "--rollback" ]]; then
  rollback
  exit 0
fi

RID="$(detect_rid)"
RELEASES="$(curl -fsSL "${API_URL}")"
TAG="$(printf '%s' "${RELEASES}" | jq -r --arg p "${TAG_PREFIX}" \
  '[.[] | select(.prerelease and (.tag_name | startswith($p)))] | sort_by(.published_at) | last | .tag_name // empty')"

if [[ -z "${TAG}" ]]; then
  log "no ${TAG_PREFIX}* release found"
  exit 0
fi

INSTALLED="$(cat "${VERSION_FILE}" 2>/dev/null || true)"
if [[ "${TAG}" = "${INSTALLED}" ]]; then
  log "already up to date (${TAG})"
  exit 0
fi
if [[ "${TAG}" = "$(cat "${FAILED_FILE}" 2>/dev/null || true)" ]]; then
  log "${TAG} failed its health check earlier, skipping until a newer release"
  exit 0
fi

ARCHIVE="printserver-${RID}.tar.gz"
asset_url() {
  local name="$1"
  printf '%s' "${RELEASES}" | jq -r --arg t "${TAG}" --arg n "${name}" \
    '.[] | select(.tag_name == $t) | .assets[] | select(.name == $n) | .browser_download_url' | head -n 1
}
ARCHIVE_URL="$(asset_url "${ARCHIVE}")"
SUMS_URL="$(asset_url SHA256SUMS)"
if [[ -z "${ARCHIVE_URL}" ]] || [[ -z "${SUMS_URL}" ]]; then
  log "release ${TAG} has no ${ARCHIVE} or SHA256SUMS"
  exit 1
fi

WORK="$(mktemp -d)"
log "downloading ${TAG} (${RID})"
curl -fsSL -o "${WORK}/${ARCHIVE}" "${ARCHIVE_URL}"
curl -fsSL -o "${WORK}/SHA256SUMS" "${SUMS_URL}"

EXPECTED="$(grep " ${ARCHIVE}\$" "${WORK}/SHA256SUMS" | awk '{print $1}')"
if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL="$(sha256sum "${WORK}/${ARCHIVE}" | awk '{print $1}')"
else
  ACTUAL="$(shasum -a 256 "${WORK}/${ARCHIVE}" | awk '{print $1}')"
fi
if [[ -z "${EXPECTED}" ]] || [[ "${EXPECTED}" != "${ACTUAL}" ]]; then
  log "checksum mismatch for ${ARCHIVE}, aborting"
  exit 1
fi

mkdir -p "${WORK}/extract"
tar -xzf "${WORK}/${ARCHIVE}" -C "${WORK}/extract"
NEW_DIR="${WORK}/extract/printserver-${RID}"
if [[ ! -x "${NEW_DIR}/PrintServer.Host" ]]; then
  log "archive does not contain an executable PrintServer.Host"
  exit 1
fi

rm -rf "${PREVIOUS_DIR}"
if [[ -d "${CURRENT_DIR}" ]]; then mv "${CURRENT_DIR}" "${PREVIOUS_DIR}"; fi
if [[ -f "${VERSION_FILE}" ]]; then cp "${VERSION_FILE}" "${PREVIOUS_VERSION_FILE}"; fi
mv "${NEW_DIR}" "${CURRENT_DIR}"
printf '%s' "${TAG}" > "${VERSION_FILE}"

restart_service
if healthy; then
  log "updated to ${TAG}"
else
  log "health check failed after updating to ${TAG}, rolling back"
  printf '%s' "${TAG}" > "${FAILED_FILE}"
  rollback
  exit 1
fi
