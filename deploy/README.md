# GhostMon deployment

This directory contains the Linux deployment assets for GhostMon. Use the root [README.md](../README.md) for build and local run instructions.

## Configuration

### Agent
- `deploy/templates/agent/appsettings.template.json`
- `deploy/templates/agent/ghostmon-agent.env.template`
- App settings keys: `DASHBOARD_BASE_URL`, `MASTER_SERVER_IP`, `SECURITY_TOKEN`, `NODE_NAME`, `GROUP_NAME`, `AGENT_PORT`
- Environment keys: `ASPNETCORE_ENVIRONMENT`

### Dashboard
- `deploy/templates/dashboard/appsettings.template.json`
- `deploy/templates/dashboard/ghostmon-dashboard.env.template`
- App settings keys: `REDIS:CONNECTIONSTRING`, `SECURITY_TOKEN`
- Environment keys: `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`

## Layout

- Agent binary: `/opt/ghostmon/agent/GhostMon.Agent`
- Dashboard binary: `/opt/ghostmon/dashboard/GhostMon.Dashboard`
- Agent env file: `/etc/ghostmon/agent.env`
- Dashboard env file: `/etc/ghostmon/dashboard.env`

## Install

1. Build or publish the services.
2. Copy the published binaries into `/opt/ghostmon/agent` and `/opt/ghostmon/dashboard`.
3. Copy the `.env.template` files to `/etc/ghostmon/agent.env` and `/etc/ghostmon/dashboard.env`.
4. Optionally copy the JSON templates beside each binary as `appsettings.json`.
5. Install the systemd units into `/etc/systemd/system/`.
6. Run `systemctl daemon-reload`.
7. Enable and start `ghostmon-dashboard.service`.
8. Enable and start `ghostmon-agent.service`.

## Helper Script

From the repository root on Linux:

```bash
bash deploy/publish-install.sh
```

The script publishes both services for `linux-x64`, copies them into `/opt/ghostmon`, installs the systemd/sysusers/tmpfiles definitions, and enables both services.