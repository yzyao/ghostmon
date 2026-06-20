using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GhostMon.Contracts;
using StackExchange.Redis;

namespace GhostMon.Dashboard;

public sealed class RedisProbeStore
{
    private const string ActiveNodesKey = "ghostmon:nodes:active";
    private const int MaxHistoryLength = 17_280;

    private readonly object _gate = new();
    private readonly IDatabase _database;
    private DashboardSnapshot _cachedSnapshot = new()
    {
        BroadcastedAtUtc = DateTimeOffset.UtcNow
    };

    public RedisProbeStore(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<DashboardSnapshot> UpsertNodeAsync(NodeRegistryRecord record, HistoricalSnapshot snapshot)
    {
        var nodeIdentity = BuildNodeIdentity(record);
        var recordJson = SerializeJson(record, ProbeJsonContext.Default.NodeRegistryRecord);
        var snapshotJson = SerializeJson(snapshot, ProbeJsonContext.Default.HistoricalSnapshot);

        var historyKey = BuildHistoryKey(record);
        var upsertNodeTask = _database.HashSetAsync(ActiveNodesKey, nodeIdentity, recordJson);
        var pushHistoryTask = _database.ListLeftPushAsync(historyKey, snapshotJson);

        await Task.WhenAll(upsertNodeTask, pushHistoryTask);
        await _database.ListTrimAsync(historyKey, 0, MaxHistoryLength - 1);
        return ApplyUpsertToCache(record, snapshot);
    }

    public DashboardSnapshot ReadDashboardSnapshot()
    {
        lock (_gate)
        {
            return _cachedSnapshot;
        }
    }

    public async Task<DashboardSnapshot> RefreshSnapshotAsync()
    {
        var nodes = await ReadAllNodesAsync();
        var snapshots = await Task.WhenAll(nodes.Select(async node =>
        {
            var history = await ReadHistoryAsync(node);
            return BuildNodeBroadcastSnapshot(node, history);
        }));

        lock (_gate)
        {
            _cachedSnapshot = new DashboardSnapshot
            {
                BroadcastedAtUtc = DateTimeOffset.UtcNow,
                Nodes = snapshots
            };

            return _cachedSnapshot;
        }
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

    public static string BuildHistoryKey(NodeRegistryRecord record) => $"ghostmon:nodes:history:{BuildNodeIdentity(record)}";

    public static string BuildNodeIdentity(NodeRegistryRecord record) => $"{record.RemoteIp}:{record.MetricsPort}";

    private DashboardSnapshot ApplyUpsertToCache(NodeRegistryRecord record, HistoricalSnapshot snapshot)
    {
        lock (_gate)
        {
            var nodeIdentity = BuildNodeIdentity(record);
            var history = PrependHistory(snapshot, FindHistory(nodeIdentity));
            var updatedNode = BuildNodeBroadcastSnapshot(record, history);

            var existingNodes = _cachedSnapshot.Nodes;
            var updatedNodes = ReplaceOrAddNode(existingNodes, updatedNode);

            _cachedSnapshot = new DashboardSnapshot
            {
                BroadcastedAtUtc = DateTimeOffset.UtcNow,
                Nodes = updatedNodes
            };

            return _cachedSnapshot;
        }
    }

    private static NodeBroadcastSnapshot BuildNodeBroadcastSnapshot(NodeRegistryRecord record, HistoricalSnapshot[] history)
    {
        var currentMetrics = record.CurrentMetrics ?? (history.Length > 0 ? history[0].Metrics : new ProbeMetrics { Assets = record.Assets });

        return new NodeBroadcastSnapshot
        {
            Registration = record,
            CurrentMetrics = currentMetrics,
            History = history
        };
    }

    private HistoricalSnapshot[] FindHistory(string nodeIdentity)
    {
        var existingNode = _cachedSnapshot.Nodes.FirstOrDefault(node =>
            BuildNodeIdentity(node.Registration) == nodeIdentity);

        return existingNode?.History ?? Array.Empty<HistoricalSnapshot>();
    }

    private static HistoricalSnapshot[] PrependHistory(HistoricalSnapshot snapshot, HistoricalSnapshot[] existingHistory)
    {
        if (existingHistory.Length == 0)
        {
            return new[] { snapshot };
        }

        var items = new HistoricalSnapshot[Math.Min(MaxHistoryLength, existingHistory.Length + 1)];
        items[0] = snapshot;

        var copyLength = items.Length - 1;
        Array.Copy(existingHistory, 0, items, 1, copyLength);
        return items;
    }

    private static NodeBroadcastSnapshot[] ReplaceOrAddNode(NodeBroadcastSnapshot[] nodes, NodeBroadcastSnapshot updatedNode)
    {
        var identity = BuildNodeIdentity(updatedNode.Registration);
        var updatedNodes = new List<NodeBroadcastSnapshot>(nodes.Length + 1);
        var replaced = false;

        foreach (var node in nodes)
        {
            if (!replaced && BuildNodeIdentity(node.Registration) == identity)
            {
                updatedNodes.Add(updatedNode);
                replaced = true;
                continue;
            }

            updatedNodes.Add(node);
        }

        if (!replaced)
        {
            updatedNodes.Add(updatedNode);
        }

        return updatedNodes
            .OrderBy(node => node.Registration.GroupName)
            .ThenBy(node => node.Registration.NodeName)
            .ThenBy(node => node.Registration.RemoteIp)
            .ToArray();
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
