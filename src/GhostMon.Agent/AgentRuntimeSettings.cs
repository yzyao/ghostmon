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
        var dashboardBaseUrl = configuration["DashboardBaseUrl"] ?? configuration["DASHBOARD_BASE_URL"];
        if (string.IsNullOrWhiteSpace(dashboardBaseUrl))
        {
            throw new InvalidOperationException("Required setting DashboardBaseUrl is missing.");
        }

        var securityToken = configuration["SecurityToken"] ?? configuration["SECURITY_TOKEN"];
        if (string.IsNullOrWhiteSpace(securityToken))
        {
            throw new InvalidOperationException("Required setting SecurityToken is missing.");
        }

        var nodeName = configuration["NodeName"] ?? configuration["NODE_NAME"];
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            throw new InvalidOperationException("Required setting NodeName is missing.");
        }

        var groupName = configuration["GroupName"] ?? configuration["GROUP_NAME"] ?? "default";
        var metricsPort = configuration.GetValue<int?>("AgentPort")
            ?? configuration.GetValue<int?>("AGENT_PORT")
            ?? 8081;
        var telemetryIntervalSeconds = configuration.GetValue<int?>("TelemetryIntervalSeconds")
            ?? configuration.GetValue<int?>("TELEMETRY_INTERVAL_SECONDS")
            ?? 5;
        var pingTimeoutMilliseconds = configuration.GetValue<int?>("PingTimeoutMilliseconds")
            ?? configuration.GetValue<int?>("PING_TIMEOUT_MILLISECONDS")
            ?? 500;
        var pingTargetModeValue = configuration["PingTargetMode"] ?? configuration["PING_TARGET_MODE"];
        var pingTargetMode = Enum.TryParse<PingTargetMode>(pingTargetModeValue, ignoreCase: true, out var parsedPingTargetMode)
            ? parsedPingTargetMode
            : PingTargetMode.Both;

        return new AgentRuntimeSettings(
            DashboardBaseUrl: dashboardBaseUrl,
            SecurityToken: securityToken,
            NodeName: nodeName,
            GroupName: groupName,
            MetricsPort: metricsPort,
            TelemetryIntervalSeconds: telemetryIntervalSeconds,
            PingTimeoutMilliseconds: pingTimeoutMilliseconds,
            PingTargetMode: pingTargetMode,
            PingTargets: GetStringArray(configuration, "PingTargets", "PING_TARGETS"),
            HostProcPath: configuration["HostProcPath"] ?? configuration["HOST_PROC_PATH"] ?? "/proc",
            HostSysPath: configuration["HostSysPath"] ?? configuration["HOST_SYS_PATH"] ?? "/sys",
            HostRootPath: configuration["HostRootPath"] ?? configuration["HOST_ROOT_PATH"] ?? "/",
            HostTmpPath: configuration["HostTmpPath"] ?? configuration["HOST_TMP_PATH"] ?? "/tmp");
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

}
