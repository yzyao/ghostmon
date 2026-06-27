using System.Net.Http.Json;
using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentTelemetryHostedService : BackgroundService
{
    private readonly AgentMetricsService _metricsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentRuntimeSettings _settings;
    private readonly AgentRuntimeState _state;
    private readonly ILogger<AgentTelemetryHostedService> _logger;

    public AgentTelemetryHostedService(
        AgentMetricsService metricsService,
        IHttpClientFactory httpClientFactory,
        AgentRuntimeSettings settings,
        AgentRuntimeState state,
        ILogger<AgentTelemetryHostedService> logger)
    {
        _metricsService = metricsService;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PushTelemetryAsync(stoppingToken);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_state.Snapshot.TelemetryIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task PushTelemetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await CreateTelemetryReportAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient(AgentEndpoints.DashboardHttpClient);
            using var response = await SendWithRetryAsync(client, payload, cancellationToken);

            if (response is null)
            {
                _logger.LogWarning("Agent telemetry push failed after retries.");
                return;
            }

            if (response.IsSuccessStatusCode)
            {
                _state.MarkAssetsReported();
                _logger.LogInformation("Agent telemetry pushed successfully.");
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Agent telemetry push failed with status code {StatusCode}: {Body}",
                (int)response.StatusCode,
                body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent telemetry push failed.");
        }
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(
        HttpClient client,
        NodeTelemetryReport payload,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        var delayMs = 100;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    new Uri(new Uri(_settings.DashboardBaseUrl.TrimEnd('/') + "/", UriKind.Absolute), AgentEndpoints.TelemetryPath.TrimStart('/')));

                request.Headers.TryAddWithoutValidation("X-Security-Token", _settings.SecurityToken);
                request.Content = JsonContent.Create(payload, ProbeJsonContext.Default.NodeTelemetryReport);

                var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode || attempt >= maxAttempts)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                if (attempt >= maxAttempts)
                {
                    return null;
                }
            }

            await Task.Delay(delayMs, cancellationToken);
            delayMs *= 2;
        }

        return null;
    }

    private async Task<NodeTelemetryReport> CreateTelemetryReportAsync(CancellationToken cancellationToken)
    {
        var metrics = await _metricsService.GetMetricsAsync(cancellationToken);
        return new NodeTelemetryReport
        {
            NodeName = _settings.NodeName,
            GroupName = _settings.GroupName,
            MetricsPort = _settings.MetricsPort,
            AgentVersion = AgentEndpoints.AgentVersion,
            Metrics = metrics
        };
    }
}
