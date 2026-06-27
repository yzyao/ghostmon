using GhostMon.Contracts;
using Microsoft.Extensions.Configuration;

namespace GhostMon.Dashboard;

public sealed record class DashboardRuntimeSettings(
    string RedisConnectionString,
    string SecurityToken,
    int TelemetryIntervalSeconds,
    int PingTimeoutMilliseconds,
    PingTargetMode PingTargetMode,
    string[] PingTargets)
{
    public static DashboardRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        var redisConnectionString = configuration["RedisConnectionString"]
            ?? configuration["REDIS:CONNECTIONSTRING"]
            ?? configuration["REDIS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("Required setting RedisConnectionString is missing.");
        }

        var securityToken = configuration["SecurityToken"] ?? configuration["SECURITY_TOKEN"];
        if (string.IsNullOrWhiteSpace(securityToken))
        {
            throw new InvalidOperationException("Required setting SecurityToken is missing.");
        }

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

        return new DashboardRuntimeSettings(
            RedisConnectionString: redisConnectionString,
            SecurityToken: securityToken,
            TelemetryIntervalSeconds: telemetryIntervalSeconds,
            PingTimeoutMilliseconds: pingTimeoutMilliseconds,
            PingTargetMode: pingTargetMode,
            PingTargets: GetStringArray(configuration, "PingTargets", "PING_TARGETS"));
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
