#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_ROOT="${PUBLISH_ROOT:-${ROOT_DIR}/artifacts}"
RUNTIME="${RUNTIME:-linux-x64}"
CONFIGURATION="${CONFIGURATION:-Release}"
INSTALL_ROOT="${INSTALL_ROOT:-/opt/ghostmon}"

publish_project() {
  local project_path="$1"
  local output_dir="$2"

  dotnet publish "${project_path}" \
    -c "${CONFIGURATION}" \
    -r "${RUNTIME}" \
    --self-contained true \
    -p:PublishAot=true \
    -o "${output_dir}"
}

copy_output() {
  local source_dir="$1"
  local target_dir="$2"

  sudo install -d -m 0755 "${target_dir}"
  sudo cp -a "${source_dir}/." "${target_dir}/"
}

main() {
  local agent_out="${PUBLISH_ROOT}/agent-${RUNTIME}"
  local dashboard_out="${PUBLISH_ROOT}/dashboard-${RUNTIME}"

  publish_project "${ROOT_DIR}/src/GhostMon.Agent/GhostMon.Agent.csproj" "${agent_out}"
  publish_project "${ROOT_DIR}/src/GhostMon.Dashboard/GhostMon.Dashboard.csproj" "${dashboard_out}"

  sudo bash "${ROOT_DIR}/deploy/install.sh"
  copy_output "${agent_out}" "${INSTALL_ROOT}/agent"
  copy_output "${dashboard_out}" "${INSTALL_ROOT}/dashboard"

  sudo systemctl restart ghostmon-dashboard.service
  sudo systemctl restart ghostmon-agent.service

  echo "Published and installed GhostMon."
  echo "Agent: ${INSTALL_ROOT}/agent"
  echo "Dashboard: ${INSTALL_ROOT}/dashboard"
}

main "$@"
