using System.Net;
using System.Text.Json;
using GhostMon.Contracts;
using StackExchange.Redis;
using GhostMon.Dashboard;

var builder = WebApplication.CreateSlimBuilder(args);
var settings = DashboardRuntimeSettings.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(settings.RedisConnectionString));
builder.Services.AddSingleton<RedisProbeStore>();
builder.Services.AddHttpClient("agent-poll", client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
});
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.TypeInfoResolver = ProbeJsonContext.Default;
    });
builder.Services.AddHostedService<MetricsPollingHostedService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Text("ok", "text/plain"));

app.MapGet("/api/snapshot", async (RedisProbeStore store) =>
{
    var snapshot = await store.ReadDashboardSnapshotAsync();
    return Results.Json(snapshot, ProbeJsonContext.Default.DashboardSnapshot);
});

app.MapPost("/api/register-node", async (HttpContext context, RedisProbeStore store, CancellationToken cancellationToken) =>
{
    if (!IsValidToken(context, settings.SecurityToken))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    NodeRegistrationRequest? request;
    try
    {
        request = await context.Request.ReadFromJsonAsync(ProbeJsonContext.Default.NodeRegistrationRequest, cancellationToken);
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
        AgentBaseUrl = BuildAgentBaseUrl(remoteIp, request.MetricsPort),
        NodeName = request.NodeName,
        GroupName = request.GroupName,
        MetricsPort = request.MetricsPort,
        AgentVersion = request.AgentVersion,
        RegisteredUtc = now,
        LastSeenUtc = now,
        Assets = request.Assets
    };

    await store.UpsertNodeAsync(record);
    return Results.Json(record, ProbeJsonContext.Default.NodeRegistryRecord);
});

app.MapHub<ProbeHub>("/hubs/probe");

app.Run();

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

static string BuildAgentBaseUrl(string remoteIp, int metricsPort)
{
    if (IPAddress.TryParse(remoteIp, out var parsed) &&
        parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        return $"http://[{parsed}]:{metricsPort}";
    }

    return $"http://{remoteIp}:{metricsPort}";
}