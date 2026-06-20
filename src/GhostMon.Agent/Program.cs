using GhostMon.Agent;
using GhostMon.Contracts;

var builder = WebApplication.CreateSlimBuilder(args);
var runtimeSettings = AgentRuntimeSettings.FromConfiguration(builder.Configuration);
var runtimeState = new AgentRuntimeState(runtimeSettings);

builder.WebHost.UseUrls($"http://0.0.0.0:{runtimeSettings.MetricsPort}");
builder.Services.AddSingleton(runtimeSettings);
builder.Services.AddSingleton(runtimeState);
builder.Services.AddHttpClient("dashboard", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<AgentMetricsService>();
builder.Services.AddHostedService<AgentTelemetryHostedService>();
builder.Services.AddHostedService<AgentConfigSyncHostedService>();

var app = builder.Build();

app.Logger.LogInformation("GhostMon Agent started on port {Port}.", runtimeSettings.MetricsPort);

app.MapGet("/healthz", static () => Results.Text("ok", "text/plain"));
app.MapGet("/metrics", MapMetrics);

app.Run();

static async Task<IResult> MapMetrics(AgentMetricsService metricsService, CancellationToken cancellationToken)
{
    var metrics = await metricsService.GetMetricsAsync(cancellationToken);
    return Results.Json(metrics, ProbeJsonContext.Default.ProbeMetrics);
}
