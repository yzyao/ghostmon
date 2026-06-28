using GhostMon.Contracts;

namespace GhostMon.Dashboard.Components.Pages;

public sealed record DashboardGroupView(string GroupName, NodeBroadcastSnapshot[] Items, int Healthy, int Warning, int Degraded);
