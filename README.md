# GhostMon

GhostMon 是一套轻量服务器探针系统，包含两个组件：

- `GhostMon.Agent`：部署在被监控机器上的 Agent
- `GhostMon.Dashboard`：部署在主控机上的 Dashboard

项目基于 `.NET 10`、`ASP.NET Core Minimal APIs` 和 `Native AOT`，JSON 序列化使用源码生成器，保持 KISS 和可部署性。

## 运行

### 本地测试

```bash
docker compose -f docker-compose.local.yml up -d --build
```

这套配置会同时启动 `Dashboard + Agent + Redis`，适合本地联调和快速验证。

### DockerHub

```bash
docker compose up -d
```

这套配置直接拉取 Docker Hub 发布镜像，适合生产部署或远程主机快速启动。
先把 `docker-compose.yml` 里的 `<your-namespace>` 替换成你的 Docker Hub 命名空间。

### Agent 直跑

Dashboard 页面会显示一条可复制的 Agent `docker run` 命令，默认使用当前 Dashboard 的 `AgentImage` 生成。

如果你只想单独启动 Agent，也可以直接复制页面上的命令，或者手动运行：

```bash
docker run -d \
  --name ghostmon-agent \
  --restart unless-stopped \
  --add-host=host.docker.internal:host-gateway \
  -p 8081:8081 \
  -e DashboardBaseUrl=http://host.docker.internal:8080 \
  -e SecurityToken=replace-with-a-shared-secret \
  -e NodeName=node-01 \
  -e GroupName=default \
  -e AgentPort=8081 \
  -e TelemetryIntervalSeconds=5 \
  -e PingTimeoutMilliseconds=500 \
  -e PingTargetMode=Both \
  -e PingTargets= \
  -e HostProcPath=/host-proc \
  -e HostSysPath=/host-sys \
  -e HostRootPath=/host-root \
  -e HostTmpPath=/host-tmp \
  -v /proc:/host-proc:ro \
  -v /sys:/host-sys:ro \
  -v /:/host-root:ro \
  -v /tmp:/host-tmp \
  docker.io/<your-namespace>/ghostmon-agent:latest
```

### 发布镜像

GitHub Actions 会在 `main` 分支推送时只做构建校验，在 `v*` 标签推送时发布到两个独立的 Docker Hub 仓库：

- `ghostmon-agent`
- `ghostmon-dashboard`

每个仓库都会带上：

- `vX.Y.Z`
- `latest`
- `sha` 构建标记

Docker Hub 需要配置以下 Secrets：

- `DOCKERHUB_USERNAME`：登录用户名
- `DOCKERHUB_TOKEN`：访问令牌
- `DOCKERHUB_NAMESPACE`：镜像仓库命名空间，通常和用户名一致，若发布到组织仓库则填组织名

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
| `AgentImage` | `docker.io/<your-namespace>/ghostmon-agent:latest` | Dashboard 页面里展示的 Agent 镜像引用 |
| `TelemetryIntervalSeconds` | `5` | 下发给 Agent 的遥测间隔 |
| `PingTimeoutMilliseconds` | `500` | 下发给 Agent 的 ping 超时 |
| `PingTargetMode` | `Both` | 下发给 Agent 的 ping 模式 |
| `PingTargets` | 空 | 留空则不执行 ping |

### Compose 部署模板

适用文件：

- `.env.example`
- `docker-compose.local.yml`
- `docker-compose.yml`

#### Shared

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `SecurityToken` | `replace-with-a-shared-secret` | Agent 与 Dashboard 共享的鉴权口令 |
| `TelemetryIntervalSeconds` | `5` | 遥测上报间隔 |
| `PingTimeoutMilliseconds` | `500` | 单次 ping 超时 |
| `PingTargetMode` | `Both` | `V4` / `V6` / `Both` |
| `PingTargets` | 空 | 留空则不执行 ping |

#### Local Compose

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `RedisConnectionString` | `redis:6379,abortConnect=false` | Compose 内 Redis 服务地址 |
| `DashboardBaseUrl` | `http://dashboard:8080` | Agent 访问 Dashboard 的内网地址 |
| `DashboardImage` | `ghostmon-dashboard:local` | Dashboard 本地构建镜像 |
| `AgentImage` | `ghostmon-agent:local` | Agent 本地构建镜像 |

#### DockerHub Compose

| Key | 默认值 | 说明 |
| --- | --- | --- |
| `RedisConnectionString` | `redis:6379,abortConnect=false` | Compose 内 Redis 服务地址 |
| `DashboardBaseUrl` | `http://dashboard:8080` | Agent 访问 Dashboard 的内网地址 |
| `DashboardImage` | `docker.io/<your-namespace>/ghostmon-dashboard:latest` | Dashboard 发布镜像 |
| `AgentImage` | `docker.io/<your-namespace>/ghostmon-agent:latest` | Agent 发布镜像 |

### 部署方式

#### 本地测试

```bash
docker compose -f docker-compose.local.yml up -d --build
```

#### DockerHub

```bash
docker compose up -d
```

Dashboard 页面会显示一条可复制的 Agent `docker run` 命令，默认使用当前 Dashboard 的 `AgentImage` 生成。

## 路由

### Agent

- `GET /healthz`
- `GET /metrics`

### Dashboard

- `GET /healthz`
- `GET /api/snapshot`
- `GET /api/agent-config`
- `GET /api/agent-install-config`
- `POST /api/ingest`
- `GET /hubs/probe`

## 说明

- Agent 不读取 `uname -r`、`/proc/version`、`/etc/os-release`
- Agent 不引用 `System.Diagnostics`
- JSON 序列化只通过 `ProbeJsonContext`
- Agent 不依赖 Redis SDK
- Dashboard 通过 Redis 保存当前节点与 24 小时历史
- Dashboard 通过 SignalR 向前端推送快照
- Dashboard 提供可复制的 Agent 安装命令
- `PingTargets` 为空时，Agent 不会发起 ping
- Agent 通过配置中心同步 `TelemetryIntervalSeconds`、`PingTimeoutMilliseconds`、`PingTargetMode` 和 `PingTargets`

## 代码风格约定

- 项目命名使用 `GhostMon`
- 类型和配置属性使用 PascalCase
- 配置键在 JSON、环境变量和代码中保持同名对齐
- Agent 侧保持 KISS，只保留最少依赖和最少职责
