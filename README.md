# GhostMon

GhostMon 是一套轻量服务器探针系统，包含两个组件：

- `GhostMon.Agent`：部署在被监控机器上的 Agent
- `GhostMon.Dashboard`：部署在主控机上的 Dashboard

项目基于 `.NET 10`、`ASP.NET Core Minimal APIs` 和 `Native AOT`，JSON 序列化使用源码生成器，保持 KISS 和可部署性。

## 运行

### Docker Compose

```bash
docker compose up -d --build
```

### 本地开发

```bash
dotnet run --project src/GhostMon.Dashboard
dotnet run --project src/GhostMon.Agent
```

## 配置模板

当前仓库里的配置入口分两类：

- 本地开发模板
- Compose 部署模板

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
| `SecurityToken` | `replace-with-a-shared-secret` | Agent 与 Dashboard 共享的鉴权口令 |
| `NodeName` | `node-01` | 节点名 |
| `GroupName` | `default` | 分组名 |
| `AgentPort` | `8081` | Agent 监听端口 |
| `TelemetryIntervalSeconds` | `5` | 遥测上报间隔 |
| `PingTimeoutMilliseconds` | `500` | 单次 ping 超时 |
| `PingTargetMode` | `Both` | `V4` / `V6` / `Both` |
| `HostProcPath` | `/proc` | 宿主机 `/proc` 挂载点 |
| `HostSysPath` | `/sys` | 宿主机 `/sys` 挂载点 |
| `HostRootPath` | `/` | 宿主机根目录挂载点 |
| `HostTmpPath` | `/tmp` | 宿主机 `/tmp` 挂载点 |
| `PingTargets` | 空 | 留空则不执行 ping |

#### Dashboard

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `RedisConnectionString` | `127.0.0.1:6379,abortConnect=false` | Redis 连接串 |
| `SecurityToken` | `replace-with-a-shared-secret` | 与 Agent 共享的鉴权口令 |
| `TelemetryIntervalSeconds` | `5` | 下发给 Agent 的遥测间隔 |
| `PingTimeoutMilliseconds` | `500` | 下发给 Agent 的 ping 超时 |
| `PingTargetMode` | `Both` | 下发给 Agent 的 ping 模式 |
| `PingTargets` | 空 | 留空则不执行 ping |

### Compose 部署模板

适用文件：

- `.env.example`
- `docker-compose.yml`

#### Shared

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `SecurityToken` | `replace-with-a-shared-secret` | Agent 与 Dashboard 共享的鉴权口令 |
| `TelemetryIntervalSeconds` | `5` | 遥测上报间隔 |
| `PingTimeoutMilliseconds` | `500` | 单次 ping 超时 |
| `PingTargetMode` | `Both` | `V4` / `V6` / `Both` |
| `PingTargets` | 空 | 留空则不执行 ping |

#### Dashboard

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `RedisConnectionString` | `redis:6379,abortConnect=false` | Compose 内 Redis 服务地址 |

#### Agent

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `DashboardBaseUrl` | `http://dashboard:8080` | Compose 内 Dashboard 服务地址 |
| `NodeName` | `node-01` | 节点名 |
| `GroupName` | `default` | 分组名 |
| `AgentPort` | `8081` | Agent 监听端口 |
| `HostProcPath` | `/host-proc` | 宿主机 `/proc` 挂载点 |
| `HostSysPath` | `/host-sys` | 宿主机 `/sys` 挂载点 |
| `HostRootPath` | `/host-root` | 宿主机根目录挂载点 |
| `HostTmpPath` | `/host-tmp` | 宿主机 `/tmp` 挂载点 |
| `PingTargets` | 空 | 留空则不执行 ping |

## 路由

### Agent

- `GET /healthz`
- `GET /metrics`

### Dashboard

- `GET /healthz`
- `GET /api/snapshot`
- `GET /api/agent-config`
- `POST /api/ingest`
- `GET /hubs/probe`

## 说明

- Agent 不读取 `uname -r`、`/proc/version`、`/etc/os-release`
- Agent 不引用 `System.Diagnostics`
- JSON 序列化只通过 `ProbeJsonContext`
- Agent 不依赖 Redis SDK
- Dashboard 通过 Redis 保存当前节点与 24 小时历史
- Dashboard 通过 SignalR 向前端推送快照
- `PingTargets` 为空时，Agent 不会发起 ping
- Agent 通过配置中心同步 `TelemetryIntervalSeconds`、`PingTimeoutMilliseconds`、`PingTargetMode` 和 `PingTargets`

## 代码风格约定

- 项目命名使用 `GhostMon`
- 类型和配置属性使用 PascalCase
- 配置键在 JSON、环境变量和代码中保持同名对齐
- Agent 侧保持 KISS，只保留最少依赖和最少职责
