using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentRuntimeState
{
    private readonly object _gate = new();
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
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Apply(AgentRuntimeConfig config)
    {
        lock (_gate)
        {
            _snapshot = new RuntimeSnapshot(
                Math.Max(1, config.TelemetryIntervalSeconds),
                Math.Max(1, config.PingTimeoutMilliseconds),
                config.PingTargetMode,
                config.PingTargets ?? Array.Empty<string>());
        }
    }

    public sealed record class RuntimeSnapshot(
        int TelemetryIntervalSeconds,
        int PingTimeoutMilliseconds,
        PingTargetMode PingTargetMode,
        string[] PingTargets);
}
