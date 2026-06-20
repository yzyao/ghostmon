namespace GhostMon.Dashboard;

internal static class DashboardConstants
{
    public const string RedisKeyPrefix = "ghostmon:nodes:";
    public const string RedisActiveNodesKey = RedisKeyPrefix + "active";
    public const string RedisHistoryKeyPrefix = RedisKeyPrefix + "history:";
    public const string SnapshotPath = "/api/snapshot";
    public const string AgentConfigPath = "/api/agent-config";
    public const string IngestPath = "/api/ingest";
    public const string HubPath = "/hubs/probe";
    public const string SnapshotUpdatedEvent = "SnapshotUpdated";
    public const string SecurityTokenHeader = "X-Security-Token";
    public const string ForwardedForHeader = "X-Forwarded-For";
}
