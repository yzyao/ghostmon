# GhostMon

GhostMon = `GhostMon.Agent` + `GhostMon.Dashboard`

## 核心流程

- Agent `POST /api/ingest` 到 Dashboard
- Agent `GET /api/agent-config` 拉取配置
- Dashboard 写 Redis，读 24 小时历史，广播给前端

## 本地运行

```bash
dotnet run --project src/GhostMon.Dashboard
dotnet run --project src/GhostMon.Agent
```

## 配置模板

### Agent

| Key | 默认值 |
| --- | --- |
| `DashboardBaseUrl` | `http://127.0.0.1:8080` |
| `SecurityToken` | `replace-with-a-shared-secret` |
| `NodeName` | `node-01` |
| `GroupName` | `default` |
| `AgentPort` | `8081` |
| `TelemetryIntervalSeconds` | `5` |
| `PingTimeoutMilliseconds` | `500` |
| `PingTargetMode` | `Both` |
| `PingTargets` | `1.1.1.1,2606:4700:4700::1111` |
| `HostProcPath` | `/proc` |
| `HostSysPath` | `/sys` |
| `HostRootPath` | `/` |
| `HostTmpPath` | `/tmp` |

### Dashboard

| Key | 默认值 |
| --- | --- |
| `RedisConnectionString` | `127.0.0.1:6379,abortConnect=false` |
| `SecurityToken` | `replace-with-a-shared-secret` |
| `TelemetryIntervalSeconds` | `5` |
| `PingTimeoutMilliseconds` | `500` |
| `PingTargetMode` | `Both` |
| `PingTargets` | `1.1.1.1,2606:4700:4700::1111` |

## 文件

- `src/GhostMon.Agent/appsettings.json`
- `src/GhostMon.Agent/Properties/launchSettings.json`
- `src/GhostMon.Dashboard/appsettings.json`
- `src/GhostMon.Dashboard/Properties/launchSettings.json`
- `.env.example`
- `docker-compose.yml`

## 接口

- `GET /healthz`
- `GET /metrics`
- `GET /api/snapshot`
- `GET /api/agent-config`
- `POST /api/ingest`

## 部署

```bash
docker compose up -d --build
```

## 约束

- Agent 不读取 `uname -r`、`/etc/os-release`、`/proc/version`
- Agent 对外平台信息固定为 `Linux (x64)`
- Agent 不引入 `System.Diagnostics`
- 所有 JSON 序列化都走 `ProbeJsonContext`
- Agent 不依赖 Redis SDK
