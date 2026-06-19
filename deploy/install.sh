#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_ROOT="${INSTALL_ROOT:-/opt/ghostmon}"
ETC_ROOT="${ETC_ROOT:-/etc/ghostmon}"
SYSTEMD_ROOT="${SYSTEMD_ROOT:-/etc/systemd/system}"
SYSUSERS_ROOT="${SYSUSERS_ROOT:-/etc/sysusers.d}"
TMPFILES_ROOT="${TMPFILES_ROOT:-/etc/tmpfiles.d}"
NGINX_ROOT="${NGINX_ROOT:-/etc/nginx/conf.d}"
USER_NAME="${USER_NAME:-ghostmon}"
GROUP_NAME="${GROUP_NAME:-ghostmon}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root." >&2
  exit 1
fi

create_user() {
  if ! getent group "${GROUP_NAME}" >/dev/null; then
    groupadd --system "${GROUP_NAME}"
  fi

  if ! id -u "${USER_NAME}" >/dev/null 2>&1; then
    useradd --system --gid "${GROUP_NAME}" --home-dir "${INSTALL_ROOT}" --shell /usr/sbin/nologin "${USER_NAME}"
  fi
}

install_layout() {
  install -d -m 0755 "${INSTALL_ROOT}/agent" "${INSTALL_ROOT}/dashboard"
  install -d -m 0700 "${ETC_ROOT}"
}

write_templates() {
  if [[ ! -f "${ETC_ROOT}/agent.env" ]]; then
    install -m 0600 "${ROOT_DIR}/templates/agent/ghostmon-agent.env.template" "${ETC_ROOT}/agent.env"
  fi

  if [[ ! -f "${ETC_ROOT}/dashboard.env" ]]; then
    install -m 0600 "${ROOT_DIR}/templates/dashboard/ghostmon-dashboard.env.template" "${ETC_ROOT}/dashboard.env"
  fi

  install -m 0644 "${ROOT_DIR}/templates/agent/appsettings.template.json" "${INSTALL_ROOT}/agent/appsettings.json"
  install -m 0644 "${ROOT_DIR}/templates/dashboard/appsettings.template.json" "${INSTALL_ROOT}/dashboard/appsettings.json"
}

install_units() {
  install -m 0644 "${ROOT_DIR}/systemd/ghostmon-agent.service" "${SYSTEMD_ROOT}/ghostmon-agent.service"
  install -m 0644 "${ROOT_DIR}/systemd/ghostmon-dashboard.service" "${SYSTEMD_ROOT}/ghostmon-dashboard.service"
}

install_systemd_users() {
  install -d -m 0755 "${SYSUSERS_ROOT}" "${TMPFILES_ROOT}"
  install -m 0644 "${ROOT_DIR}/sysusers.d/ghostmon.conf" "${SYSUSERS_ROOT}/ghostmon.conf"
  install -m 0644 "${ROOT_DIR}/tmpfiles.d/ghostmon.conf" "${TMPFILES_ROOT}/ghostmon.conf"

  if command -v systemd-sysusers >/dev/null 2>&1; then
    systemd-sysusers
  fi

  if command -v systemd-tmpfiles >/dev/null 2>&1; then
    systemd-tmpfiles --create
  fi
}

install_nginx() {
  if [[ -d "${NGINX_ROOT}" ]]; then
    install -m 0644 "${ROOT_DIR}/nginx/ghostmon-dashboard.conf" "${NGINX_ROOT}/ghostmon-dashboard.conf"
  fi
}

main() {
  create_user
  install_layout
  write_templates
  install_systemd_users
  install_units
  install_nginx

  systemctl daemon-reload
  systemctl enable ghostmon-dashboard.service
  systemctl enable ghostmon-agent.service

  echo "Installed GhostMon. Edit ${ETC_ROOT}/agent.env and ${ETC_ROOT}/dashboard.env if needed."
  echo "Start with: systemctl start ghostmon-dashboard.service ghostmon-agent.service"
}

main "$@"
