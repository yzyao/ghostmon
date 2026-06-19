using System.Net;
using GhostMon.Agent;
using GhostMon.Contracts;

var builder = WebApplication.CreateSlimBuilder(args);
var settings = AgentRuntimeSettings.FromConfiguration(builder.Configuration);

builder.WebHost.UseUrls($"http://0.0.0.0:{settings.MetricsPort}");
builder.Services.AddSingleton(settings);
builder.Services.AddHttpClient("self-probe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddHttpClient("registration", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<AgentMetricsService>();
builder.Services.AddHostedService<AgentRegistrationHostedService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (IsAllowedMaster(context.Connection.RemoteIpAddress, settings.MasterServerIp))
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status403Forbidden;
});

app.MapGet("/metrics", async (AgentMetricsService metricsService, CancellationToken cancellationToken) =>
{
    var metrics = await metricsService.GetMetricsAsync(cancellationToken);
    return Results.Json(metrics, ProbeJsonContext.Default.ProbeMetrics);
});

app.Run();

static bool IsAllowedMaster(IPAddress? remoteAddress, string masterServerIp)
{
    var remote = NormalizeIp(remoteAddress?.ToString());
    var allowed = NormalizeIp(masterServerIp);
    return remote is not null &&
           allowed is not null &&
           string.Equals(remote, allowed, StringComparison.Ordinal);
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