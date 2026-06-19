using System.Net.Http.Json;
using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentRegistrationHostedService : IHostedService
{
    private readonly AgentMetricsService _metricsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentRuntimeSettings _settings;
    private readonly ILogger<AgentRegistrationHostedService> _logger;

    public AgentRegistrationHostedService(
        AgentMetricsService metricsService,
        IHttpClientFactory httpClientFactory,
        AgentRuntimeSettings settings,
        ILogger<AgentRegistrationHostedService> logger)
    {
        _metricsService = metricsService;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = new NodeRegistrationRequest
            {
                NodeName = _settings.NodeName,
                GroupName = _settings.GroupName,
                MetricsPort = _settings.MetricsPort,
                Assets = _metricsService.Assets
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri($"{_settings.DashboardBaseUrl.TrimEnd('/')}/api/register-node"));

            request.Headers.TryAddWithoutValidation("X-Security-Token", _settings.SecurityToken);
            request.Content = JsonContent.Create(payload, ProbeJsonContext.Default.NodeRegistrationRequest);

            var client = _httpClientFactory.CreateClient("registration");
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Probe node registered successfully with dashboard.");
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning(
                "Probe node registration failed with {StatusCode}: {Body}",
                (int)response.StatusCode,
                body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Probe node registration failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}