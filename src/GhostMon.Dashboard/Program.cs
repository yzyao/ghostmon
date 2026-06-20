using System.Net;
using System.Text.Json;
using GhostMon.Contracts;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using GhostMon.Dashboard;

var builder = WebApplication.CreateSlimBuilder(args);
var runtimeSettings = DashboardRuntimeSettings.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(runtimeSettings);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(runtimeSettings.RedisConnectionString));
builder.Services.AddSingleton<RedisProbeStore>();
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.TypeInfoResolver = ProbeJsonContext.Default;
    });

var app = builder.Build();

await app.Services.GetRequiredService<RedisProbeStore>().RefreshSnapshotAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", static () => Results.Text("ok", "text/plain"));
app.MapGet("/api/snapshot", MapSnapshot);
app.MapGet("/api/agent-config", GetAgentConfig);
app.MapPost("/api/ingest", IngestNode);
app.MapHub<ProbeHub>("/hubs/probe");

app.Logger.LogInformation("GhostMon Dashboard started.");

app.Run();

static IResult MapSnapshot(RedisProbeStore store)
{
    return Results.Json(store.ReadDashboardSnapshot(), ProbeJsonContext.Default.DashboardSnapshot);
}

static IResult GetAgentConfig(DashboardRuntimeSettings runtimeSettings)
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

static async Task<IResult> IngestNode(
    HttpContext context,
    RedisProbeStore store,
    IHubContext<ProbeHub> hubContext,
    DashboardRuntimeSettings runtimeSettings,
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
        Metrics = request.Metrics
    };

    var dashboardSnapshot = await store.UpsertNodeAsync(record, snapshot);
    await hubContext.Clients.All.SendAsync("SnapshotUpdated", dashboardSnapshot, cancellationToken);

    return Results.NoContent();
}

static bool IsValidToken(HttpContext context, string expectedToken)
{
    var incomingToken = context.Request.Headers["X-Security-Token"].ToString();
    return !string.IsNullOrWhiteSpace(incomingToken) &&
           string.Equals(incomingToken, expectedToken, StringComparison.Ordinal);
}

static string ResolveClientIp(HttpContext context)
{
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
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

static string? NormalizeIp(string? raw)
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
