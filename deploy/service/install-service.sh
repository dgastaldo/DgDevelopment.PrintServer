#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/printserver}"
SERVICE_NAME="${SERVICE_NAME:-printserver}"
SERVICE_USER="${SERVICE_USER:-printserver}"
LAUNCHD_LABEL="${LAUNCHD_LABEL:-com.printserver}"
UNIT_DIR="${UNIT_DIR:-/etc/systemd/system}"
PLIST_DIR="${PLIST_DIR:-/Library/LaunchDaemons}"
SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
START=1

for argument in "$@"; do
  case "${argument}" in
    --no-start) START=0 ;;
    *) echo "unknown argument: ${argument}" >&2; exit 2 ;;
  esac
done

log() { printf '%s\n' "$*"; }

if [[ "${EUID}" -ne 0 ]]; then
  log "run this script as root (sudo)"
  exit 1
fi

if [[ ! -x "${INSTALL_DIR}/current/PrintServer.Host" ]]; then
  log "warning: ${INSTALL_DIR}/current/PrintServer.Host not found; install a release first (deploy/personal/update.sh)"
fi

install_config() {
  local directory="$1"
  mkdir -p "${directory}"
  if [[ ! -f "${directory}/appsettings.json" ]]; then
    cp "${SOURCE_DIR}/appsettings.example.json" "${directory}/appsettings.json"
    log "created ${directory}/appsettings.json from the example: edit it before relying on the service"
  fi
}

install_linux() {
  local config_dir="${CONFIG_DIR:-/etc/printserver}"
  if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
    useradd --system --home-dir /var/lib/printserver --shell /usr/sbin/nologin "${SERVICE_USER}"
    log "created system user ${SERVICE_USER}"
  fi
  install_config "${config_dir}"
  sed -e "s#^User=.*#User=${SERVICE_USER}#" \
      -e "s#^Group=.*#Group=${SERVICE_USER}#" \
      -e "s#/opt/printserver#${INSTALL_DIR}#g" \
      "${SOURCE_DIR}/printserver.service" > "${UNIT_DIR}/${SERVICE_NAME}.service"
  log "installed ${UNIT_DIR}/${SERVICE_NAME}.service"
  if [[ "${START}" -eq 1 ]]; then
    systemctl daemon-reload
    systemctl enable --now "${SERVICE_NAME}"
    log "service ${SERVICE_NAME} enabled and started"
  fi
}

install_macos() {
  local config_dir="${CONFIG_DIR:-/Library/Application Support/PrintServer}"
  install_config "${config_dir}"
  sed -e "s#<string>com.printserver</string>#<string>${LAUNCHD_LABEL}</string>#" \
      -e "s#/opt/printserver#${INSTALL_DIR}#g" \
      "${SOURCE_DIR}/com.printserver.plist" > "${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
  chown root:wheel "${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
  chmod 644 "${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
  log "installed ${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
  if [[ "${START}" -eq 1 ]]; then
    launchctl bootout "system/${LAUNCHD_LABEL}" >/dev/null 2>&1 || true
    launchctl bootstrap system "${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
    log "service ${LAUNCHD_LABEL} loaded"
  fi
}

case "$(uname -s)" in
  Linux) install_linux ;;
  Darwin) install_macos ;;
  *) log "unsupported platform $(uname -s)"; exit 1 ;;
esac
