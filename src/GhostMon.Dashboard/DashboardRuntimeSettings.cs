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
        return new DashboardRuntimeSettings(
            RedisConnectionString: GetRequiredString(configuration, "RedisConnectionString", "REDIS:CONNECTIONSTRING", "REDIS_CONNECTION_STRING"),
            SecurityToken: GetRequiredString(configuration, "SecurityToken", "SECURITY_TOKEN"),
            TelemetryIntervalSeconds: GetInt(configuration, 5, "TelemetryIntervalSeconds", "TELEMETRY_INTERVAL_SECONDS"),
            PingTimeoutMilliseconds: GetInt(configuration, 500, "PingTimeoutMilliseconds", "PING_TIMEOUT_MILLISECONDS"),
            PingTargetMode: GetPingTargetMode(configuration, PingTargetMode.Both, "PingTargetMode", "PING_TARGET_MODE"),
            PingTargets: GetStringArray(configuration, "PingTargets", "PING_TARGETS"));
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
