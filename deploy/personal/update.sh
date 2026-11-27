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

# Messages come in English (the default) and Italian; the keys are English so the
# catalog stays readable even where a translation is missing.
# Language: PRINTSERVER_LANG, then the system locale, then English.
default_language() {
  local value
  for value in "${PRINTSERVER_LANG:-}" "${LC_ALL:-}" "${LC_MESSAGES:-}" "${LANG:-}"; do
    case "${value}" in
      it | it_* | it-* | it.*) echo it; return ;;
      en | en_* | en-* | en.*) echo en; return ;;
      *) ;;
    esac
  done
  echo en
}

LANGUAGE_CODE="$(default_language)"

# text KEY: the format string of a message in the current language.
text() {
  local key="$1"
  case "${LANGUAGE_CODE}:${key}" in
    it:unsupported_platform) echo 'piattaforma non supportata %s' ;;
    *:unsupported_platform) echo 'unsupported platform %s' ;;
    it:launchd_not_found) echo 'servizio launchd %s non trovato, riavvialo a mano' ;;
    *:launchd_not_found) echo 'launchd service %s not found, restart it manually' ;;
    it:service_not_found) echo 'servizio %s non trovato, riavvialo a mano' ;;
    *:service_not_found) echo 'service %s not found, restart it manually' ;;
    it:no_previous_version) echo 'nessuna versione precedente a cui tornare' ;;
    *:no_previous_version) echo 'no previous version to roll back to' ;;
    it:rolled_back) echo 'tornato alla versione %s' ;;
    *:rolled_back) echo 'rolled back to %s' ;;
    it:another_update_running) echo 'un altro aggiornamento è in corso' ;;
    *:another_update_running) echo 'another update is running' ;;
    it:no_release) echo 'nessuna release %s* trovata' ;;
    *:no_release) echo 'no %s* release found' ;;
    it:up_to_date) echo 'già aggiornato (%s)' ;;
    *:up_to_date) echo 'already up to date (%s)' ;;
    it:failed_health_before) echo '%s non ha superato il controllo di salute in precedenza, la salto fino a una release più recente' ;;
    *:failed_health_before) echo '%s failed its health check earlier, skipping until a newer release' ;;
    it:missing_assets) echo 'la release %s non contiene %s o SHA256SUMS' ;;
    *:missing_assets) echo 'release %s has no %s or SHA256SUMS' ;;
    it:downloading) echo 'scarico %s (%s)' ;;
    *:downloading) echo 'downloading %s (%s)' ;;
    it:checksum_mismatch) echo 'checksum diversa per %s, interrompo' ;;
    *:checksum_mismatch) echo 'checksum mismatch for %s, aborting' ;;
    it:no_executable) echo 'l'"'"'archivio non contiene un PrintServer.Host eseguibile' ;;
    *:no_executable) echo 'archive does not contain an executable PrintServer.Host' ;;
    it:updated) echo 'aggiornato a %s' ;;
    *:updated) echo 'updated to %s' ;;
    it:health_failed_rollback) echo 'controllo di salute fallito dopo l'"'"'aggiornamento a %s, torno indietro' ;;
    *:health_failed_rollback) echo 'health check failed after updating to %s, rolling back' ;;
    *) echo "${key}" ;;
  esac
}

# say KEY [ARGS...]: log a message, filling in its %s placeholders.
say() {
  local key="$1"
  shift
  # shellcheck disable=SC2059
  log "$(printf -- "$(text "${key}")" "$@")"
}

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "${os}/${arch}" in
    Linux/x86_64) echo linux-x64 ;;
    Linux/aarch64 | Linux/arm64) echo linux-arm64 ;;
    Darwin/arm64) echo osx-arm64 ;;
    Darwin/x86_64) echo osx-x64 ;;
    *) say unsupported_platform "${os}/${arch}"; exit 1 ;;
  esac
}

restart_service() {
  if [[ "$(uname -s)" = "Darwin" ]]; then
    if launchctl print "system/${LAUNCHD_LABEL}" >/dev/null 2>&1; then
      launchctl kickstart -k "system/${LAUNCHD_LABEL}"
    else
      say launchd_not_found "${LAUNCHD_LABEL}"
    fi
  elif command -v systemctl >/dev/null 2>&1 && systemctl cat "${SERVICE_NAME}" >/dev/null 2>&1; then
    systemctl restart "${SERVICE_NAME}"
  else
    say service_not_found "${SERVICE_NAME}"
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
    say no_previous_version
    exit 1
  fi
  local failed="${INSTALL_DIR}/failed"
  rm -rf "${failed}"
  mv "${CURRENT_DIR}" "${failed}"
  mv "${PREVIOUS_DIR}" "${CURRENT_DIR}"
  if [[ -f "${PREVIOUS_VERSION_FILE}" ]]; then mv "${PREVIOUS_VERSION_FILE}" "${VERSION_FILE}"; fi
  rm -rf "${failed}"
  restart_service
  say rolled_back "$(cat "${VERSION_FILE}" 2>/dev/null || echo unknown)"
}

mkdir -p "${INSTALL_DIR}"
if ! mkdir "${LOCK_DIR}" 2>/dev/null; then
  say another_update_running
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
  say no_release "${TAG_PREFIX}"
  exit 0
fi

INSTALLED="$(cat "${VERSION_FILE}" 2>/dev/null || true)"
if [[ "${TAG}" = "${INSTALLED}" ]]; then
  say up_to_date "${TAG}"
  exit 0
fi
if [[ "${TAG}" = "$(cat "${FAILED_FILE}" 2>/dev/null || true)" ]]; then
  say failed_health_before "${TAG}"
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
  say missing_assets "${TAG}" "${ARCHIVE}"
  exit 1
fi

WORK="$(mktemp -d)"
say downloading "${TAG}" "${RID}"
curl -fsSL -o "${WORK}/${ARCHIVE}" "${ARCHIVE_URL}"
curl -fsSL -o "${WORK}/SHA256SUMS" "${SUMS_URL}"

EXPECTED="$(grep " ${ARCHIVE}\$" "${WORK}/SHA256SUMS" | awk '{print $1}')"
if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL="$(sha256sum "${WORK}/${ARCHIVE}" | awk '{print $1}')"
else
  ACTUAL="$(shasum -a 256 "${WORK}/${ARCHIVE}" | awk '{print $1}')"
fi
if [[ -z "${EXPECTED}" ]] || [[ "${EXPECTED}" != "${ACTUAL}" ]]; then
  say checksum_mismatch "${ARCHIVE}"
  exit 1
fi

mkdir -p "${WORK}/extract"
tar -xzf "${WORK}/${ARCHIVE}" -C "${WORK}/extract"
NEW_DIR="${WORK}/extract/printserver-${RID}"
if [[ ! -x "${NEW_DIR}/PrintServer.Host" ]]; then
  say no_executable
  exit 1
fi

rm -rf "${PREVIOUS_DIR}"
if [[ -d "${CURRENT_DIR}" ]]; then mv "${CURRENT_DIR}" "${PREVIOUS_DIR}"; fi
if [[ -f "${VERSION_FILE}" ]]; then cp "${VERSION_FILE}" "${PREVIOUS_VERSION_FILE}"; fi
mv "${NEW_DIR}" "${CURRENT_DIR}"
printf '%s' "${TAG}" > "${VERSION_FILE}"

restart_service
if healthy; then
  say updated "${TAG}"
else
  say health_failed_rollback "${TAG}"
  printf '%s' "${TAG}" > "${FAILED_FILE}"
  rollback
  exit 1
fi
