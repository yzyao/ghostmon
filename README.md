# GhostMon

GhostMon = `GhostMon.Agent` + `GhostMon.Dashboard`

## 运行

```bash
docker compose up -d --build
```

本地开发：

```bash
dotnet run --project src/GhostMon.Dashboard
dotnet run --project src/GhostMon.Agent
```

## 配置模板

### 本地开发模板

适用文件：

- `src/GhostMon.Agent/appsettings.json`
- `src/GhostMon.Agent/Properties/launchSettings.json`
- `src/GhostMon.Dashboard/appsettings.json`
- `src/GhostMon.Dashboard/Properties/launchSettings.json`

#### Agent

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `DashboardBaseUrl` | `http://127.0.0.1:8080` | Dashboard 地址 |
| `SecurityToken` | `replace-with-a-shared-secret` | Agent 和 Dashboard 共用 |
| `NodeName` | `node-01` | 节点名 |
| `GroupName` | `default` | 分组名 |
| `AgentPort` | `8081` | Agent 监听端口 |
| `TelemetryIntervalSeconds` | `5` | 轮询和上报间隔 |
| `PingTimeoutMilliseconds` | `500` | 单次 ping 超时 |
| `PingTargetMode` | `Both` | `V4` / `V6` / `Both` |
| `PingTargets` | 空 | 留空则不执行 ping |
| `HostProcPath` | `/proc` | 宿主机 `/proc` 挂载点 |
| `HostSysPath` | `/sys` | 宿主机 `/sys` 挂载点 |
| `HostRootPath` | `/` | 宿主机根目录挂载点 |
| `HostTmpPath` | `/tmp` | 宿主机 `/tmp` 挂载点 |

#### Dashboard

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `RedisConnectionString` | `127.0.0.1:6379,abortConnect=false` | Redis 连接串 |
| `SecurityToken` | `replace-with-a-shared-secret` | Dashboard 和 Agent 共用 |
| `TelemetryIntervalSeconds` | `5` | 轮询和上报间隔 |
| `PingTimeoutMilliseconds` | `500` | 单次 ping 超时 |
| `PingTargetMode` | `Both` | `V4` / `V6` / `Both` |
| `PingTargets` | 空 | 留空则不执行 ping |

### Compose 模板

适用文件：

- `.env.example`
- `docker-compose.yml`

#### Shared

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `SecurityToken` | `replace-with-a-shared-secret` | Agent 和 Dashboard 共用 |
| `TelemetryIntervalSeconds` | `5` | 轮询和上报间隔 |
| `PingTimeoutMilliseconds` | `500` | 单次 ping 超时 |
| `PingTargetMode` | `Both` | `V4` / `V6` / `Both` |
| `PingTargets` | 空 | 留空则不执行 ping |

#### Dashboard

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `RedisConnectionString` | `redis:6379,abortConnect=false` | Redis 服务名连接串 |

#### Agent

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `DashboardBaseUrl` | `http://dashboard:8080` | Dashboard 服务名地址 |
| `NodeName` | `node-01` | 节点名 |
| `GroupName` | `default` | 分组名 |
| `AgentPort` | `8081` | Agent 监听端口 |
| `HostProcPath` | `/host-proc` | 宿主机 `/proc` 挂载点 |
| `HostSysPath` | `/host-sys` | 宿主机 `/sys` 挂载点 |
| `HostRootPath` | `/host-root` | 宿主机根目录挂载点 |
| `HostTmpPath` | `/host-tmp` | 宿主机 `/tmp` 挂载点 |

## 路由

- `GET /healthz`
- `GET /metrics`
- `GET /api/snapshot`
- `GET /api/agent-config`
- `POST /api/ingest`
- `GET /hubs/probe`

## 说明

- Agent 不读取 `uname -r`、`/proc/version`、`/etc/os-release`
- Agent 不引用 `System.Diagnostics`
- JSON 序列化只走 `ProbeJsonContext`
- Agent 不依赖 Redis SDK
- Dashboard 通过 SignalR 向前端推送快照
- `PingTargets` 留空时，Agent 不会发起 ping
