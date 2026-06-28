using System.IO.Compression;
using System.Text;
using GhostMon.Contracts;
using MudBlazor.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using StackExchange.Redis;
using GhostMon.Dashboard;
using GhostMon.Dashboard.Components;

var builder = WebApplication.CreateSlimBuilder(args);
var runtimeSettings = DashboardRuntimeSettings.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(runtimeSettings);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(runtimeSettings.RedisConnectionString));
builder.Services.AddSingleton<RedisProbeStore>();
builder.Services.AddSingleton<IProbeStore>(sp => sp.GetRequiredService<RedisProbeStore>());
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet(DashboardConstants.SnapshotPath, DashboardEndpoints.MapSnapshotForTests);
app.MapGet(DashboardConstants.NodeDetailPath, DashboardEndpoints.GetNodeDetailForTests);
app.MapGet(DashboardConstants.AgentConfigPath, DashboardEndpoints.GetAgentConfigForTests);
app.MapGet(DashboardConstants.AgentInstallConfigPath, DashboardEndpoints.GetAgentInstallConfigForTests);
app.MapPost(DashboardConstants.IngestPath, DashboardEndpoints.IngestNodeForTests);

app.MapGet("/demo-check", static () => Results.Content("""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>GhostMon Demo</title>
</head>
<body>
    <h1>GhostMon Demo</h1>
    <p>This endpoint is a plain server-side check.</p>
</body>
</html>
""", "text/html"));

app.MapGet("/routes-check", (IEnumerable<EndpointDataSource> sources) =>
{
    var routes = sources
        .SelectMany(source => source.Endpoints)
        .OfType<RouteEndpoint>()
        .Select(endpoint => new
        {
            Pattern = endpoint.RoutePattern.RawText,
            DisplayName = endpoint.DisplayName
        })
        .OrderBy(item => item.Pattern)
        .ToArray();

    var sb = new StringBuilder();
    foreach (var route in routes)
    {
        sb.AppendLine($"{route.Pattern} | {route.DisplayName}");
    }

    return Results.Text(sb.ToString(), "text/plain");
});

app.MapGet("/healthz", static () => Results.Text("ok", "text/plain"));

app.Logger.LogInformation("GhostMon Dashboard started.");

app.Run();
