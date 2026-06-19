using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using GhostMon.Contracts;

namespace GhostMon.Dashboard;

public sealed class MetricsPollingHostedService : BackgroundService
{
    private readonly RedisProbeStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<ProbeHub> _hubContext;
    private readonly ILogger<MetricsPollingHostedService> _logger;

    public MetricsPollingHostedService(
        RedisProbeStore store,
        IHttpClientFactory httpClientFactory,
        IHubContext<ProbeHub> hubContext,
        ILogger<MetricsPollingHostedService> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollOnceAsync(stoppingToken);

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var nodes = await _store.ReadAllNodesAsync();
        var client = _httpClientFactory.CreateClient("agent-poll");
        var refreshedNodes = await Task.WhenAll(nodes.Select(node => PollNodeAsync(client, node, cancellationToken)));

        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await _store.ReadDashboardSnapshotAsync(refreshedNodes);
        await _hubContext.Clients.All.SendAsync("SnapshotUpdated", snapshot, cancellationToken);
    }

    private async Task<NodeRegistryRecord> PollNodeAsync(HttpClient client, NodeRegistryRecord node, CancellationToken cancellationToken)
    {
        try
        {
            var metricsUri = new Uri($"{node.AgentBaseUrl.TrimEnd('/')}/metrics");
            using var response = await client.GetAsync(metricsUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Probe node {NodeName} at {RemoteIp} returned {StatusCode}.", node.NodeName, node.RemoteIp, (int)response.StatusCode);
                return node;
            }

            var metrics = await response.Content.ReadFromJsonAsync(ProbeJsonContext.Default.ProbeMetrics, cancellationToken);
            if (metrics is null)
            {
                return node;
            }

            var capturedAt = DateTimeOffset.UtcNow;
            var refreshedNode = node with { LastSeenUtc = capturedAt };
            var historicalSnapshot = new HistoricalSnapshot
            {
                CapturedAtUtc = capturedAt,
                Metrics = metrics
            };

            await _store.UpsertNodeAsync(refreshedNode);
            await _store.AppendHistoryAsync(refreshedNode, historicalSnapshot);
            return refreshedNode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to poll probe node {NodeName} at {RemoteIp}.", node.NodeName, node.RemoteIp);
            return node;
        }
    }
}