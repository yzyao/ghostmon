namespace GhostMon.Contracts;

public sealed record class ProbeAssetsInfo
{
    public string CpuModelName { get; init; } = string.Empty;

    public string OsPlatform { get; init; } = "Linux (x64)";

    public int MemoryTotalMb { get; init; }

    public int SwapTotalMb { get; init; }

    public int DiskTotalGb { get; init; }
}

public readonly record struct ProbeRuntimeInfo
{
    public long UpTimeSeconds { get; init; }

    public double CpuUsedPercent { get; init; }

    public double MemoryUsedPercent { get; init; }

    public double SwapUsedPercent { get; init; }

    public double DiskUsedPercent { get; init; }

    public long DiskReadBytesPerSec { get; init; }

    public long DiskWriteBytesPerSec { get; init; }

    public double NetRxSpeedKbps { get; init; }

    public double NetTxSpeedKbps { get; init; }

    public long BootTotalRxBytes { get; init; }

    public long BootTotalTxBytes { get; init; }

    public int? PingV4DelayMs { get; init; }

    public int? PingV6DelayMs { get; init; }
}

public sealed record class ProbeSanityInfo
{
    public string? PublicIPv4 { get; init; }

    public string? PublicIPv6 { get; init; }

    public bool DiskReadOnlyPanic { get; init; }

    public bool ChatGPTUnlocked { get; init; }
}

public sealed record class ProbeMetrics
{
    public ProbeAssetsInfo Assets { get; init; } = new();

    public ProbeRuntimeInfo Runtime { get; init; }

    public ProbeSanityInfo Sanity { get; init; } = new();
}

public sealed record class NodeRegistrationRequest
{
    public string NodeName { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public int MetricsPort { get; init; } = 8081;

    public string AgentVersion { get; init; } = "GhostMon.Agent/1.0.0";

    public ProbeAssetsInfo Assets { get; init; } = new();
}

public sealed record class NodeRegistryRecord
{
    public string RemoteIp { get; init; } = string.Empty;

    public string AgentBaseUrl { get; init; } = string.Empty;

    public string NodeName { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public int MetricsPort { get; init; } = 8081;

    public string AgentVersion { get; init; } = "GhostMon.Agent/1.0.0";

    public DateTimeOffset RegisteredUtc { get; init; }

    public DateTimeOffset LastSeenUtc { get; init; }

    public ProbeAssetsInfo Assets { get; init; } = new();
}

public sealed record class HistoricalSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; }

    public ProbeMetrics Metrics { get; init; } = new();
}

public sealed record class NodeBroadcastSnapshot
{
    public NodeRegistryRecord Registration { get; init; } = new();

    public ProbeMetrics CurrentMetrics { get; init; } = new();

    public HistoricalSnapshot[] History { get; init; } = Array.Empty<HistoricalSnapshot>();
}

public sealed record class DashboardSnapshot
{
    public DateTimeOffset BroadcastedAtUtc { get; init; }

    public NodeBroadcastSnapshot[] Nodes { get; init; } = Array.Empty<NodeBroadcastSnapshot>();
}
