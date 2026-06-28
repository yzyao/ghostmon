using GhostMon.Contracts;

namespace GhostMon.Dashboard;

public interface IProbeStore
{
    DashboardSnapshot ReadDashboardSnapshot();

    Task<NodeDetailSnapshot?> ReadNodeDetailAsync(string remoteIp, int metricsPort);
}
