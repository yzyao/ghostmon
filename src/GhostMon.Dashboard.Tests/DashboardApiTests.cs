using System.Text.Json;
using GhostMon.Contracts;
using GhostMon.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GhostMon.Dashboard.Tests;

public sealed class DashboardApiTests
{
    [Fact]
    public async Task Snapshot_Response_Does_Not_Include_History()
    {
        var snapshot = new DashboardSnapshot
        {
            BroadcastedAtUtc = DateTimeOffset.UtcNow,
            Nodes =
            [
                new NodeBroadcastSnapshot
                {
                    Registration = CreateRegistration(),
                    CurrentMetrics = CreateMetrics()
                }
            ]
        };

        var result = DashboardEndpoints.MapSnapshotForTests(new FakeStore(snapshot));
        var json = await ExecuteJsonAsync(result);

        Assert.Contains("\"nodes\"", json);
        Assert.Contains("\"currentMetrics\"", json);
        Assert.DoesNotContain("\"history\"", json);
    }

    [Fact]
    public async Task NodeDetail_Response_Includes_History_For_Node_Identity()
    {
        var record = CreateRegistration();
        var detail = new NodeDetailSnapshot
        {
            Registration = record,
            CurrentMetrics = CreateMetrics(),
            History =
            [
                new HistoricalSnapshot
                {
                    CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    Runtime = CreateMetrics().Runtime
                }
            ]
        };

        var result = await DashboardEndpoints.GetNodeDetailForTests(
            new FakeStore(nodeDetail: detail),
            record.RemoteIp,
            record.MetricsPort);

        var json = await ExecuteJsonAsync(result);

        Assert.Contains("\"history\"", json);
        Assert.Contains("\"capturedAtUtc\"", json);
        Assert.Contains(record.RemoteIp, json);
    }

    [Fact]
    public async Task NodeDetail_Response_Returns_NotFound_When_Node_Missing()
    {
        var result = await DashboardEndpoints.GetNodeDetailForTests(new FakeStore(), "10.0.0.1", 8081);

        Assert.IsType<NotFound>(result);
    }

    private static NodeRegistryRecord CreateRegistration() =>
        new()
        {
            RemoteIp = "10.0.0.1",
            NodeName = "node-1",
            GroupName = "default",
            MetricsPort = 8081,
            RegisteredUtc = DateTimeOffset.UtcNow.AddHours(-1),
            LastSeenUtc = DateTimeOffset.UtcNow
        };

    private static ProbeMetrics CreateMetrics() =>
        new()
        {
            Assets = new ProbeAssetsInfo
            {
                CpuModelName = "Test CPU",
                OsPlatform = "Linux"
            },
            Runtime = new ProbeRuntimeInfo
            {
                CpuUsedPercent = 12.5,
                MemoryUsedPercent = 22.5,
                NetRxSpeedKbps = 1.5,
                NetTxSpeedKbps = 2.5
            },
            Sanity = new ProbeSanityInfo()
        };

    private static async Task<string> ExecuteJsonAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;

        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeStore : IProbeStore
    {
        private readonly DashboardSnapshot _snapshot;
        private readonly NodeDetailSnapshot? _nodeDetail;

        public FakeStore(DashboardSnapshot? snapshot = null, NodeDetailSnapshot? nodeDetail = null)
        {
            _snapshot = snapshot ?? new DashboardSnapshot { BroadcastedAtUtc = DateTimeOffset.UtcNow };
            _nodeDetail = nodeDetail;
        }

        public DashboardSnapshot ReadDashboardSnapshot() => _snapshot;

        public Task RefreshSnapshotAsync() => Task.CompletedTask;

        public Task<NodeDetailSnapshot?> ReadNodeDetailAsync(string remoteIp, int metricsPort)
        {
            if (_nodeDetail is null)
            {
                return Task.FromResult<NodeDetailSnapshot?>(null);
            }

            var record = _nodeDetail.Registration;
            if (record.RemoteIp == remoteIp && record.MetricsPort == metricsPort)
            {
                return Task.FromResult<NodeDetailSnapshot?>(_nodeDetail);
            }

            return Task.FromResult<NodeDetailSnapshot?>(null);
        }
    }
}
