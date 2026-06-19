using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using StackExchange.Redis;
using GhostMon.Contracts;

namespace GhostMon.Dashboard;

public sealed class RedisProbeStore
{
    private const string ActiveNodesKey = "ghostmon:nodes:active";

    private readonly IDatabase _database;

    public RedisProbeStore(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public Task UpsertNodeAsync(NodeRegistryRecord record)
    {
        return _database.HashSetAsync(
            ActiveNodesKey,
            BuildNodeIdentity(record),
            SerializeJson(record, ProbeJsonContext.Default.NodeRegistryRecord));
    }

    public async Task<NodeRegistryRecord[]> ReadAllNodesAsync()
    {
        var entries = await _database.HashGetAllAsync(ActiveNodesKey);
        var nodes = new List<NodeRegistryRecord>(entries.Length);

        foreach (var entry in entries)
        {
            var node = TryDeserializeJson(entry.Value, ProbeJsonContext.Default.NodeRegistryRecord);
            if (node is not null)
            {
                nodes.Add(node);
            }
        }

        return nodes
            .OrderBy(node => node.GroupName)
            .ThenBy(node => node.NodeName)
            .ThenBy(node => node.RemoteIp)
            .ToArray();
    }

    public Task AppendHistoryAsync(NodeRegistryRecord record, HistoricalSnapshot snapshot)
    {
        return AppendHistoryCoreAsync(
            BuildHistoryKey(record),
            SerializeJson(snapshot, ProbeJsonContext.Default.HistoricalSnapshot));
    }

    public async Task<HistoricalSnapshot[]> ReadHistoryAsync(NodeRegistryRecord record)
    {
        var values = await _database.ListRangeAsync(BuildHistoryKey(record), 0, -1);
        var history = new List<HistoricalSnapshot>(values.Length);

        foreach (var value in values)
        {
            var snapshot = TryDeserializeJson(value, ProbeJsonContext.Default.HistoricalSnapshot);
            if (snapshot is not null)
            {
                history.Add(snapshot);
            }
        }

        return history.ToArray();
    }

    public async Task<DashboardSnapshot> ReadDashboardSnapshotAsync()
    {
        var nodes = await ReadAllNodesAsync();
        return await ReadDashboardSnapshotAsync(nodes);
    }

    public async Task<DashboardSnapshot> ReadDashboardSnapshotAsync(IReadOnlyList<NodeRegistryRecord> nodes)
    {
        var snapshots = await Task.WhenAll(nodes.Select(async node =>
        {
            var history = await ReadHistoryAsync(node);
            var currentMetrics = history.Length > 0
                ? history[0].Metrics with { Assets = node.Assets }
                : new ProbeMetrics { Assets = node.Assets };

            return new NodeBroadcastSnapshot
            {
                Registration = node,
                CurrentMetrics = currentMetrics,
                History = history
            };
        }));

        return new DashboardSnapshot
        {
            BroadcastedAtUtc = DateTimeOffset.UtcNow,
            Nodes = snapshots
        };
    }

    public static string BuildHistoryKey(NodeRegistryRecord record) => $"ghostmon:nodes:history:{BuildNodeIdentity(record)}";

    public static string BuildNodeIdentity(NodeRegistryRecord record) => $"{record.RemoteIp}:{record.MetricsPort}";

    private async Task AppendHistoryCoreAsync(string key, string serialized)
    {
        await _database.ListLeftPushAsync(key, serialized);
        await _database.ListTrimAsync(key, 0, 17_279);
    }

    private static string SerializeJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.Serialize(value, typeInfo);
    }

    private static T? TryDeserializeJson<T>(RedisValue value, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}