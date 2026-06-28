using System.Net;
using System.Text.Json;
using GhostMon.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GhostMon.Dashboard;

public static class DashboardEndpoints
{
    public static IResult MapSnapshotForTests([FromServices] IProbeStore store)
    {
        return Results.Json(store.ReadDashboardSnapshot(), ProbeJsonContext.Default.DashboardSnapshot);
    }

    public static async Task<IResult> GetNodeDetailForTests(
        [FromServices] IProbeStore store,
        string remoteIp,
        int metricsPort)
    {
        if (string.IsNullOrWhiteSpace(remoteIp) || metricsPort <= 0)
        {
            return Results.BadRequest();
        }

        var detail = await store.ReadNodeDetailAsync(remoteIp, metricsPort);
        return detail is null
            ? Results.NotFound()
            : Results.Json(detail, ProbeJsonContext.Default.NodeDetailSnapshot);
    }

    public static IResult GetAgentConfigForTests([FromServices] DashboardRuntimeSettings runtimeSettings)
    {
        var config = new AgentRuntimeConfig
        {
            TelemetryIntervalSeconds = runtimeSettings.TelemetryIntervalSeconds,
            PingTimeoutMilliseconds = runtimeSettings.PingTimeoutMilliseconds,
            PingTargetMode = runtimeSettings.PingTargetMode,
            PingTargets = runtimeSettings.PingTargets
        };

        return Results.Json(config, ProbeJsonContext.Default.AgentRuntimeConfig);
    }

    public static IResult GetAgentInstallConfigForTests([FromServices] DashboardRuntimeSettings runtimeSettings)
    {
        var config = new AgentInstallConfig
        {
            AgentImage = runtimeSettings.AgentImage,
            SecurityToken = runtimeSettings.SecurityToken,
            TelemetryIntervalSeconds = runtimeSettings.TelemetryIntervalSeconds,
            PingTimeoutMilliseconds = runtimeSettings.PingTimeoutMilliseconds,
            PingTargetMode = runtimeSettings.PingTargetMode,
            PingTargets = runtimeSettings.PingTargets
        };

        return Results.Json(config, ProbeJsonContext.Default.AgentInstallConfig);
    }

    public static async Task<IResult> IngestNodeForTests(
        HttpContext context,
        [FromServices] IProbeStore store,
        [FromServices] DashboardRuntimeSettings runtimeSettings,
        CancellationToken cancellationToken)
    {
        if (!IsValidToken(context, runtimeSettings.SecurityToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        NodeTelemetryReport? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(ProbeJsonContext.Default.NodeTelemetryReport, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (request is null)
        {
            return Results.BadRequest();
        }

        var remoteIp = ResolveClientIp(context);
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            return Results.BadRequest();
        }

        if (store is not RedisProbeStore redisStore)
        {
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        }

        var now = DateTimeOffset.UtcNow;
        var record = new NodeRegistryRecord
        {
            RemoteIp = remoteIp,
            NodeName = request.NodeName,
            GroupName = request.GroupName,
            MetricsPort = request.MetricsPort,
            AgentVersion = request.AgentVersion,
            RegisteredUtc = now,
            LastSeenUtc = now,
            Assets = request.Metrics.Assets,
            CurrentMetrics = request.Metrics
        };

        var snapshot = new HistoricalSnapshot
        {
            CapturedAtUtc = now,
            Runtime = request.Metrics.Runtime
        };

        await redisStore.UpsertNodeAsync(record, snapshot);
        return Results.NoContent();
    }

    private static bool IsValidToken(HttpContext context, string expectedToken)
    {
        var incomingToken = context.Request.Headers[DashboardConstants.SecurityTokenHeader].ToString();
        return !string.IsNullOrWhiteSpace(incomingToken) &&
               string.Equals(incomingToken, expectedToken, StringComparison.Ordinal);
    }

    private static string ResolveClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers[DashboardConstants.ForwardedForHeader].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var first = forwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var normalizedForwarded = NormalizeIp(first);
            if (!string.IsNullOrWhiteSpace(normalizedForwarded))
            {
                return normalizedForwarded;
            }
        }

        return NormalizeIp(context.Connection.RemoteIpAddress?.ToString()) ?? string.Empty;
    }

    private static string? NormalizeIp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!IPAddress.TryParse(raw, out var parsed))
        {
            return raw.Trim();
        }

        if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && parsed.IsIPv4MappedToIPv6)
        {
            return parsed.MapToIPv4().ToString();
        }

        return parsed.ToString();
    }
}
