using System.Net.Http.Json;
using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentConfigSyncHostedService : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentRuntimeSettings _settings;
    private readonly AgentRuntimeState _state;
    private readonly ILogger<AgentConfigSyncHostedService> _logger;

    public AgentConfigSyncHostedService(
        IHttpClientFactory httpClientFactory,
        AgentRuntimeSettings settings,
        AgentRuntimeState state,
        ILogger<AgentConfigSyncHostedService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAsync(stoppingToken);
            await AgentBackgroundWorker.DelayAsync(SyncInterval, stoppingToken);
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(AgentEndpoints.DashboardHttpClient);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_settings.DashboardBaseUrl.TrimEnd('/') + "/"), AgentEndpoints.AgentConfigPath.TrimStart('/')));
            request.Headers.TryAddWithoutValidation("X-Security-Token", _settings.SecurityToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent config sync failed with status code {StatusCode}.",
                    (int)response.StatusCode);
                return;
            }

            var config = await response.Content.ReadFromJsonAsync(
                ProbeJsonContext.Default.AgentRuntimeConfig,
                cancellationToken);

            if (config is null)
            {
                _logger.LogWarning("Agent config sync returned empty payload.");
                return;
            }

            _state.Apply(config);
            _logger.LogInformation(
                "Agent config synced: interval={Interval}s pingMode={PingMode}.",
                config.TelemetryIntervalSeconds,
                config.PingTargetMode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent config sync failed.");
        }
    }
}
