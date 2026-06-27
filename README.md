# GhostMon

GhostMon 是一套轻量服务器探针系统，包含两个组件：

- `GhostMon.Agent`：部署在被监控机器上的 Agent
- `GhostMon.Dashboard`：部署在主控机上的 Dashboard

项目基于 `.NET 10`、`ASP.NET Core Minimal APIs` 和 `Native AOT`，保持依赖少、部署简单。

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
先把 [`docker-compose.yml`](C:/Users/yzyao/Documents/ghostmon/docker-compose.yml) 里的 `<your-namespace>` 替换成你的 Docker Hub 命名空间。

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

## 配置

### 本地测试

本地测试不再需要独立的 `.env.example`，直接使用 `docker-compose.local.yml` 里的默认值即可。

可按需覆盖的常用变量：

- `RedisConnectionString=redis:6379,abortConnect=false`
- `DashboardBaseUrl=http://dashboard:8080`
- `DashboardImage=ghostmon-dashboard:local`
- `AgentImage=ghostmon-agent:local`
- `SecurityToken=replace-with-a-shared-secret`
- `TelemetryIntervalSeconds=5`
- `PingTimeoutMilliseconds=500`
- `PingTargetMode=Both`
- `PingTargets=`
- `NodeName=node-01`
- `GroupName=default`
- `AgentPort=8081`
- `HostProcPath=/host-proc`
- `HostSysPath=/host-sys`
- `HostRootPath=/host-root`
- `HostTmpPath=/host-tmp`

### DockerHub

线上部署使用 [`docker-compose.yml`](C:/Users/yzyao/Documents/ghostmon/docker-compose.yml)。

可按需覆盖的常用变量：

- `DashboardImage=docker.io/<your-namespace>/ghostmon-dashboard:latest`
- `AgentImage=docker.io/<your-namespace>/ghostmon-agent:latest`
- `SecurityToken=replace-with-a-shared-secret`
- `TelemetryIntervalSeconds=5`
- `PingTimeoutMilliseconds=500`
- `PingTargetMode=Both`
- `PingTargets=`
- `RedisConnectionString=redis:6379,abortConnect=false`
- `DashboardBaseUrl=http://dashboard:8080`

### Dashboard 安装接口

Dashboard 提供可复制 Agent 命令的安装配置接口：

- `GET /api/agent-install-config`

## 发布镜像

GitHub Actions 会在 `main` 分支推送时只做构建校验，在 `v*` 标签推送时发布到两个独立的 Docker Hub 仓库：

- `ghostmon-agent`
- `ghostmon-dashboard`

每个仓库都会带上：

- `vX.Y.Z`
- `latest`
- `sha` 构建标记

需要配置的 GitHub Secrets：

- `DOCKERHUB_USERNAME`：登录用户名
- `DOCKERHUB_TOKEN`：访问令牌
- `DOCKERHUB_NAMESPACE`：镜像仓库命名空间，通常和用户名一致，若发布到组织仓库则填组织名

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
