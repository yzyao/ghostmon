using Microsoft.Extensions.Configuration;

namespace GhostMon.Agent;

public sealed record class AgentRuntimeSettings(
    string DashboardBaseUrl,
    string MasterServerIp,
    string SecurityToken,
    string NodeName,
    string GroupName,
    int MetricsPort)
{
    public static AgentRuntimeSettings FromConfiguration(IConfiguration configuration)
    {
        return new AgentRuntimeSettings(
            DashboardBaseUrl: Require(configuration, "DASHBOARD_BASE_URL"),
            MasterServerIp: Require(configuration, "MASTER_SERVER_IP"),
            SecurityToken: Require(configuration, "SECURITY_TOKEN"),
            NodeName: GetString(configuration, "NODE_NAME", Environment.MachineName),
            GroupName: GetString(configuration, "GROUP_NAME", "default"),
            MetricsPort: GetInt(configuration, "AGENT_PORT", 8081));
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{key} is required.")
            : value;
    }

    private static string GetString(IConfiguration configuration, string key, string fallback)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int GetInt(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration[key];
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}