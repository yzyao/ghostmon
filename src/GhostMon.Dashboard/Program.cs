using System.Net;
using System.IO.Compression;
using System.Text.Json;
using GhostMon.Contracts;
using Microsoft.AspNetCore.ResponseCompression;
using StackExchange.Redis;
using GhostMon.Dashboard;

var builder = WebApplication.CreateSlimBuilder(args);
var runtimeSettings = DashboardRuntimeSettings.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(runtimeSettings);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(runtimeSettings.RedisConnectionString));
builder.Services.AddSingleton<RedisProbeStore>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});

var app = builder.Build();

await app.Services.GetRequiredService<RedisProbeStore>().RefreshSnapshotAsync();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", static () => Results.Text("ok", "text/plain"));
app.MapGet(DashboardConstants.SnapshotPath, MapSnapshot);
app.MapGet(DashboardConstants.NodeDetailPath, GetNodeDetail);
app.MapGet(DashboardConstants.AgentConfigPath, GetAgentConfig);
app.MapGet(DashboardConstants.AgentInstallConfigPath, GetAgentInstallConfig);
app.MapPost(DashboardConstants.IngestPath, IngestNode);

app.Logger.LogInformation("GhostMon Dashboard started.");

app.Run();

static IResult MapSnapshot(RedisProbeStore store)
{
    return DashboardEndpoints.MapSnapshotForTests(store);
}

static Task<IResult> GetNodeDetail(string remoteIp, int metricsPort, RedisProbeStore store)
{
    return DashboardEndpoints.GetNodeDetailForTests(store, remoteIp, metricsPort);
}

static IResult GetAgentConfig(DashboardRuntimeSettings runtimeSettings)
{
    return DashboardEndpoints.GetAgentConfigForTests(runtimeSettings);
}

static IResult GetAgentInstallConfig(DashboardRuntimeSettings runtimeSettings)
{
    return DashboardEndpoints.GetAgentInstallConfigForTests(runtimeSettings);
}

static Task<IResult> IngestNode(
    HttpContext context,
    RedisProbeStore store,
    DashboardRuntimeSettings runtimeSettings,
    CancellationToken cancellationToken)
{
    return DashboardEndpoints.IngestNodeForTests(context, store, runtimeSettings, cancellationToken);
}
