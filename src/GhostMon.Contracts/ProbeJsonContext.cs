using System.Text.Json.Serialization;

namespace GhostMon.Contracts;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(ProbeAssetsInfo))]
[JsonSerializable(typeof(ProbeRuntimeInfo))]
[JsonSerializable(typeof(ProbeSanityInfo))]
[JsonSerializable(typeof(ProbeMetrics))]
[JsonSerializable(typeof(NodeTelemetryReport))]
[JsonSerializable(typeof(AgentRuntimeConfig))]
[JsonSerializable(typeof(AgentInstallConfig))]
[JsonSerializable(typeof(PingTargetMode))]
[JsonSerializable(typeof(NodeRegistryRecord))]
[JsonSerializable(typeof(HistoricalSnapshot))]
[JsonSerializable(typeof(NodeBroadcastSnapshot))]
[JsonSerializable(typeof(NodeDetailSnapshot))]
[JsonSerializable(typeof(DashboardSnapshot))]
public partial class ProbeJsonContext : JsonSerializerContext;
