using GhostMon.Contracts;
using Microsoft.Extensions.Configuration;

namespace GhostMon.Agent;

public sealed record class AgentRuntimeSettings(
    string DashboardBaseUrl,
    string SecurityToken,
    string NodeName,
    string GroupName,
    int MetricsPort,
    int TelemetryIntervalSeconds,
    int PingTimeoutMilliseconds,
    PingTargetMode PingTargetMode,
    string[] PingTargets,
    string HostProcPath,
    string HostSysPath,
    string HostRootPath,
    string HostTmpPath)
{
    public static AgentRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        return new AgentRuntimeSettings(
            DashboardBaseUrl: GetRequiredString(configuration, "DashboardBaseUrl", "DASHBOARD_BASE_URL"),
            SecurityToken: GetRequiredString(configuration, "SecurityToken", "SECURITY_TOKEN"),
            NodeName: GetRequiredString(configuration, "NodeName", "NODE_NAME"),
            GroupName: GetStringOrDefault(configuration, "default", "GroupName", "GROUP_NAME"),
            MetricsPort: GetInt(configuration, 8081, "AgentPort", "AGENT_PORT"),
            TelemetryIntervalSeconds: GetInt(configuration, 5, "TelemetryIntervalSeconds", "TELEMETRY_INTERVAL_SECONDS"),
            PingTimeoutMilliseconds: GetInt(configuration, 500, "PingTimeoutMilliseconds", "PING_TIMEOUT_MILLISECONDS"),
            PingTargetMode: GetPingTargetMode(configuration, PingTargetMode.Both, "PingTargetMode", "PING_TARGET_MODE"),
            PingTargets: GetStringArray(configuration, "PingTargets", "PING_TARGETS"),
            HostProcPath: GetStringOrDefault(configuration, "/proc", "HostProcPath", "HOST_PROC_PATH"),
            HostSysPath: GetStringOrDefault(configuration, "/sys", "HostSysPath", "HOST_SYS_PATH"),
            HostRootPath: GetStringOrDefault(configuration, "/", "HostRootPath", "HOST_ROOT_PATH"),
            HostTmpPath: GetStringOrDefault(configuration, "/tmp", "HostTmpPath", "HOST_TMP_PATH"));
    }

    private static string GetRequiredString(IConfiguration configuration, params string[] keys)
    {
        var value = GetStringOrDefault(configuration, string.Empty, keys);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Required setting {keys[0]} is missing.");
    }

    private static string GetStringOrDefault(IConfiguration configuration, string fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return fallback;
    }

    private static int GetInt(IConfiguration configuration, int fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (int.TryParse(value, out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static string[] GetStringArray(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var sectionValues = configuration.GetSection(key).GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray();

            if (sectionValues.Length > 0)
            {
                return sectionValues;
            }

            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return Array.Empty<string>();
    }

    private static PingTargetMode GetPingTargetMode(IConfiguration configuration, PingTargetMode fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (Enum.TryParse<PingTargetMode>(value, ignoreCase: true, out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }
}
