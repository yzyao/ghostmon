# GhostMon

GhostMon is a lightweight .NET 10 probe system built with ASP.NET Core Minimal APIs, Native AOT, Redis, and SignalR.

## Project Layout

- `src/GhostMon.Agent` - probe agent
- `src/GhostMon.Dashboard` - dashboard and history store
- `src/GhostMon.Contracts` - shared DTOs and source-generated JSON context
- `deploy` - Linux deployment templates and scripts
- `docker-compose.yml` - dashboard plus Redis local stack

## Build

```bash
dotnet build GhostMon.sln -c Release
```

## Configuration

### Agent
- `src/GhostMon.Agent/appsettings.json` or `deploy/templates/agent/appsettings.template.json`
- `deploy/templates/agent/ghostmon-agent.env.template`
- App settings keys: `DASHBOARD_BASE_URL`, `MASTER_SERVER_IP`, `SECURITY_TOKEN`, `NODE_NAME`, `GROUP_NAME`, `AGENT_PORT`
- Environment keys: `ASPNETCORE_ENVIRONMENT`

### Dashboard
- `src/GhostMon.Dashboard/appsettings.json` or `deploy/templates/dashboard/appsettings.template.json`
- `deploy/templates/dashboard/ghostmon-dashboard.env.template`
- App settings keys: `REDIS:CONNECTIONSTRING`, `SECURITY_TOKEN`
- Environment keys: `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`

## Local Run

1. Start Redis and Dashboard:

```bash
docker compose up -d redis dashboard
```

2. Start the Agent after setting its config:

```bash
dotnet run --project src/GhostMon.Agent
```

3. Open the dashboard at `http://localhost:8080`.

## Linux Deploy

See [deploy/README.md](deploy/README.md) for the Linux install companion.

Use the provided templates and systemd units:

- `deploy/templates/agent/appsettings.template.json`
- `deploy/templates/agent/ghostmon-agent.env.template`
- `deploy/templates/dashboard/appsettings.template.json`
- `deploy/templates/dashboard/ghostmon-dashboard.env.template`
- `deploy/systemd/ghostmon-agent.service`
- `deploy/systemd/ghostmon-dashboard.service`
- `deploy/sysusers.d/ghostmon.conf`
- `deploy/tmpfiles.d/ghostmon.conf`
- `deploy/nginx/ghostmon-dashboard.conf`
- `deploy/install.sh`
- `deploy/publish-install.sh`

Install flow:

1. Copy the published binaries into `/opt/ghostmon/agent` and `/opt/ghostmon/dashboard`.
2. Copy the `.env.template` files to `/etc/ghostmon/agent.env` and `/etc/ghostmon/dashboard.env`.
3. Optionally copy the JSON templates beside each binary as `appsettings.json`.
4. Install the systemd units and run `systemctl daemon-reload`.
5. Enable and start `ghostmon-dashboard.service`.
6. Enable and start `ghostmon-agent.service`.

## Notes

- Public .NET names use `GhostMon`.
- Linux service and directory names keep the lowercase `ghostmon`.
- Agent does not reference Redis.
- JSON serialization uses source-generated contexts for AOT compatibility.
- Dashboard uses StackExchange.Redis and SignalR for live state and history broadcast.