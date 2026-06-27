using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentRuntimeState
{
    private int _assetsReported;
    private RuntimeSnapshot _snapshot;

    public AgentRuntimeState(AgentRuntimeSettings settings)
    {
        _snapshot = new RuntimeSnapshot(
            Math.Max(1, settings.TelemetryIntervalSeconds),
            Math.Max(1, settings.PingTimeoutMilliseconds),
            settings.PingTargetMode,
            settings.PingTargets ?? Array.Empty<string>());
    }

    public RuntimeSnapshot Snapshot
    {
        get => Volatile.Read(ref _snapshot);
    }

    public void Apply(AgentRuntimeConfig config)
    {
        Volatile.Write(
            ref _snapshot,
            new RuntimeSnapshot(
                Math.Max(1, config.TelemetryIntervalSeconds),
                Math.Max(1, config.PingTimeoutMilliseconds),
                config.PingTargetMode,
                config.PingTargets ?? Array.Empty<string>()));
    }

    public bool ShouldIncludeAssets()
    {
        return Volatile.Read(ref _assetsReported) == 0;
    }

    public void MarkAssetsReported()
    {
        Interlocked.Exchange(ref _assetsReported, 1);
    }

    public sealed record class RuntimeSnapshot(
        int TelemetryIntervalSeconds,
        int PingTimeoutMilliseconds,
        PingTargetMode PingTargetMode,
        string[] PingTargets);
}
