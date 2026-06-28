# GhostMon

GhostMon 是一套轻量服务器探针系统，包含两个服务：

- `GhostMon.Agent`：部署在被监控机器上
- `GhostMon.Dashboard`：部署在主控机上

项目基于 `.NET 10`、ASP.NET Core Minimal APIs 和 Native AOT。

## 运行

### 本地联调

```bash
docker compose -f docker-compose.local.yml up -d --build
```

这套配置会同时启动 Dashboard、Agent 和 Redis，适合本地联调。

### 生产部署

```bash
docker compose up -d
```

这套配置会直接拉取 Docker Hub 镜像。先把 `docker-compose.yml` 里的 `<your-namespace>` 替换成你的 Docker Hub 命名空间。

Redis 不暴露宿主机端口，只在 Docker 内网里通过 `redis:6379` 访问。

### Agent 单独安装

Dashboard 页面会生成一条可复制的 `docker run` 命令。

也可以手动运行：

```bash
docker run -d \
  --name ghostmon-agent \
  --restart unless-stopped \
  --add-host=host.docker.internal:host-gateway \
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

### 本地联调

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

### Docker Hub

- `DashboardImage=docker.io/<your-namespace>/ghostmon-dashboard:latest`
- `AgentImage=docker.io/<your-namespace>/ghostmon-agent:latest`
- `SecurityToken=replace-with-a-shared-secret`
- `TelemetryIntervalSeconds=5`
- `PingTimeoutMilliseconds=500`
- `PingTargetMode=Both`
- `PingTargets=`
- `RedisConnectionString=redis:6379,abortConnect=false`
- `DashboardBaseUrl=http://dashboard:8080`

### Dashboard 接口

- `GET /api/agent-install-config`

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

## 说明

- Agent 不读取 `uname -r`、`/proc/version`、`/etc/os-release`
- Agent 不引入 `System.Diagnostics`
- JSON 序列化只通过 `ProbeJsonContext`
- Dashboard 使用 Redis 保存节点状态和历史
- Dashboard 通过 HTTP 响应压缩降低 `/api/snapshot` 传输体积
- Redis 只在 Docker 内网里访问，不暴露宿主机端口
- `PingTargets` 为空时，Agent 不会发起 ping
- Agent 通过配置中心同步 `TelemetryIntervalSeconds`、`PingTimeoutMilliseconds`、`PingTargetMode` 和 `PingTargets`
