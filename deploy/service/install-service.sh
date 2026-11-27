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

# Messages come in English (the default) and Italian. The keys are English so the
# catalog stays readable even where a translation is missing.
# Language: --lang en|it, then PRINTSERVER_LANG, then the system locale, then English.
language_from() {
  case "${1:-}" in
    it | it_* | it-* | it.*) echo it ;;
    en | en_* | en-* | en.*) echo en ;;
    *) echo "" ;;
  esac
}

default_language() {
  local found
  for value in "${PRINTSERVER_LANG:-}" "${LC_ALL:-}" "${LC_MESSAGES:-}" "${LANG:-}"; do
    found="$(language_from "${value}")"
    if [[ -n "${found}" ]]; then
      echo "${found}"
      return
    fi
  done
  echo en
}

LANGUAGE_CODE="$(default_language)"

# text KEY: the format string of a message in the current language.
text() {
  local key="$1"
  case "${LANGUAGE_CODE}:${key}" in
    it:usage) echo 'uso: install-service.sh [--lang en|it] [--no-start]' ;;
    *:usage) echo 'usage: install-service.sh [--lang en|it] [--no-start]' ;;
    it:unknown_argument) echo 'argomento sconosciuto: %s' ;;
    *:unknown_argument) echo 'unknown argument: %s' ;;
    it:unknown_language) echo 'lingua non supportata: %s (usa en o it)' ;;
    *:unknown_language) echo 'unsupported language: %s (use en or it)' ;;
    it:missing_language) echo '--lang richiede un valore (en o it)' ;;
    *:missing_language) echo '--lang needs a value (en or it)' ;;
    it:run_as_root) echo 'esegui questo script come root (sudo)' ;;
    *:run_as_root) echo 'run this script as root (sudo)' ;;
    it:release_missing) echo 'attenzione: %s non trovato; installa prima una versione (deploy/personal/update.sh)' ;;
    *:release_missing) echo 'warning: %s not found; install a release first (deploy/personal/update.sh)' ;;
    it:config_created) echo 'creato %s dall'"'"'esempio: modificalo prima di fare affidamento sul servizio' ;;
    *:config_created) echo 'created %s from the example: edit it before relying on the service' ;;
    it:user_created) echo 'creato l'"'"'utente di sistema %s' ;;
    *:user_created) echo 'created system user %s' ;;
    it:unit_installed) echo 'installato %s' ;;
    *:unit_installed) echo 'installed %s' ;;
    it:service_started) echo 'servizio %s abilitato e avviato' ;;
    *:service_started) echo 'service %s enabled and started' ;;
    it:plist_installed) echo 'installato %s' ;;
    *:plist_installed) echo 'installed %s' ;;
    it:service_loaded) echo 'servizio %s caricato' ;;
    *:service_loaded) echo 'service %s loaded' ;;
    it:unsupported_platform) echo 'piattaforma non supportata %s' ;;
    *:unsupported_platform) echo 'unsupported platform %s' ;;
    *) echo "${key}" ;;
  esac
}

# say KEY [ARGS...]: print a message, filling in its %s placeholders.
say() {
  local key="$1"
  shift
  # shellcheck disable=SC2059
  printf -- "$(text "${key}")\n" "$@"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-start) START=0 ;;
    --help | -h) say usage; exit 0 ;;
    --lang)
      if [[ $# -lt 2 ]]; then say missing_language >&2; exit 2; fi
      shift
      LANGUAGE_CODE="$(language_from "$1")"
      if [[ -z "${LANGUAGE_CODE}" ]]; then LANGUAGE_CODE=en; say unknown_language "$1" >&2; exit 2; fi
      ;;
    --lang=*)
      LANGUAGE_CODE="$(language_from "${1#--lang=}")"
      if [[ -z "${LANGUAGE_CODE}" ]]; then LANGUAGE_CODE=en; say unknown_language "${1#--lang=}" >&2; exit 2; fi
      ;;
    *) say unknown_argument "$1" >&2; exit 2 ;;
  esac
  shift
done

if [[ "${EUID}" -ne 0 ]]; then
  say run_as_root
  exit 1
fi

if [[ ! -x "${INSTALL_DIR}/current/PrintServer.Host" ]]; then
  say release_missing "${INSTALL_DIR}/current/PrintServer.Host"
fi

install_config() {
  local directory="$1"
  mkdir -p "${directory}"
  if [[ ! -f "${directory}/appsettings.json" ]]; then
    cp "${SOURCE_DIR}/appsettings.example.json" "${directory}/appsettings.json"
    say config_created "${directory}/appsettings.json"
  fi
}

install_linux() {
  local config_dir="${CONFIG_DIR:-/etc/printserver}"
  if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
    useradd --system --home-dir /var/lib/printserver --shell /usr/sbin/nologin "${SERVICE_USER}"
    say user_created "${SERVICE_USER}"
  fi
  install_config "${config_dir}"
  sed -e "s#^User=.*#User=${SERVICE_USER}#" \
      -e "s#^Group=.*#Group=${SERVICE_USER}#" \
      -e "s#/opt/printserver#${INSTALL_DIR}#g" \
      "${SOURCE_DIR}/printserver.service" > "${UNIT_DIR}/${SERVICE_NAME}.service"
  say unit_installed "${UNIT_DIR}/${SERVICE_NAME}.service"
  if [[ "${START}" -eq 1 ]]; then
    systemctl daemon-reload
    systemctl enable --now "${SERVICE_NAME}"
    say service_started "${SERVICE_NAME}"
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
  say plist_installed "${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
  if [[ "${START}" -eq 1 ]]; then
    launchctl bootout "system/${LAUNCHD_LABEL}" >/dev/null 2>&1 || true
    launchctl bootstrap system "${PLIST_DIR}/${LAUNCHD_LABEL}.plist"
    say service_loaded "${LAUNCHD_LABEL}"
  fi
}

case "$(uname -s)" in
  Linux) install_linux ;;
  Darwin) install_macos ;;
  *) say unsupported_platform "$(uname -s)"; exit 1 ;;
esac
