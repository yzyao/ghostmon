using Microsoft.Extensions.Configuration;

namespace GhostMon.Dashboard;

public sealed record class DashboardRuntimeSettings(
    string RedisConnectionString,
    string SecurityToken)
{
    public static DashboardRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        return new DashboardRuntimeSettings(
            RedisConnectionString: Require(configuration, "REDIS:CONNECTIONSTRING"),
            SecurityToken: Require(configuration, "SECURITY_TOKEN"));
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{key} is required.")
            : value;
    }
}