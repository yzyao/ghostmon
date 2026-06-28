using System.IO.Compression;
using GhostMon.Contracts;
using MudBlazor.Services;
using Microsoft.AspNetCore.ResponseCompression;
using StackExchange.Redis;
using GhostMon.Dashboard;
using GhostMon.Dashboard.Components;

var builder = WebApplication.CreateSlimBuilder(args);
var runtimeSettings = DashboardRuntimeSettings.FromConfiguration(builder.Configuration);

builder.Services.AddSingleton(runtimeSettings);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(runtimeSettings.RedisConnectionString));
builder.Services.AddSingleton<RedisProbeStore>();
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

app.MapGet("/healthz", static () => Results.Text("ok", "text/plain"));

app.Logger.LogInformation("GhostMon Dashboard started.");

app.Run();
