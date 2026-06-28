using GhostMon.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace GhostMon.Dashboard.Components.Pages;

[Route("/")]
public partial class Index : ComponentBase
{
    protected const string HealthFilterAll = "all";
    protected const string HealthFilterHealthy = "healthy";
    protected const string HealthFilterWarning = "warning";
    protected const string HealthFilterDegraded = "degraded";

    [Inject]
    public required IProbeStore ProbeStore { get; set; }

    [Inject]
    public required DashboardRuntimeSettings RuntimeSettings { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    [Inject]
    public required IJSRuntime JsRuntime { get; set; }

    protected DashboardSnapshot? Snapshot { get; private set; }
    protected readonly Dictionary<string, NodeDetailSnapshot> NodeDetails = new(StringComparer.Ordinal);
    protected readonly HashSet<string> LoadingDetails = new(StringComparer.Ordinal);
    protected readonly Dictionary<string, bool> NodeOpen = new(StringComparer.Ordinal);
    protected readonly Dictionary<string, bool> GroupOpen = new(StringComparer.Ordinal);

    private string _search = string.Empty;
    private string _healthFilter = HealthFilterAll;

    protected string Search
    {
        get => _search;
        set
        {
            if (_search == value)
            {
                return;
            }

            _search = value ?? string.Empty;
            RecomputeDerivedState();
        }
    }

    protected string HealthFilter
    {
        get => _healthFilter;
        set
        {
            if (_healthFilter == value)
            {
                return;
            }

            _healthFilter = value;
            RecomputeDerivedState();
        }
    }

    protected bool CollapseAll { get; private set; }
    protected bool SnapshotLoading { get; private set; }
    protected string ClusterValue { get; private set; } = "N/A";
    protected string ClusterDetail { get; private set; } = "Waiting for the first snapshot.";
    protected string HealthValue { get; private set; } = "Unknown";
    protected string HealthDetail { get; private set; } = "Waiting for snapshot refresh.";
    protected Color HealthColor { get; private set; } = Color.Default;
    protected string TrafficValue { get; private set; } = "0 kbps";
    protected string TrafficDetail { get; private set; } = "Rx 0 kbps / Tx 0 kbps";
    protected string FilterSummary { get; private set; } = "No filter is active.";
    protected string AgentInstallStatus { get; private set; } = "Waiting to generate install command.";
    protected string InstallCommand { get; private set; } = "Loading...";
    protected string ToggleGroupsText { get; private set; } = "Collapse all";

    protected IReadOnlyList<DashboardGroupView> VisibleGroups => GroupVisibleNodes();

    protected IReadOnlyList<DashboardFilters.FilterOption> FilterOptions => new[]
    {
        new DashboardFilters.FilterOption("All", FilterButtonVariant(HealthFilterAll), EventCallback.Factory.Create(this, () => SetHealthFilter(HealthFilterAll))),
        new DashboardFilters.FilterOption("Healthy", FilterButtonVariant(HealthFilterHealthy), EventCallback.Factory.Create(this, () => SetHealthFilter(HealthFilterHealthy))),
        new DashboardFilters.FilterOption("Warning", FilterButtonVariant(HealthFilterWarning), EventCallback.Factory.Create(this, () => SetHealthFilter(HealthFilterWarning))),
        new DashboardFilters.FilterOption("Degraded", FilterButtonVariant(HealthFilterDegraded), EventCallback.Factory.Create(this, () => SetHealthFilter(HealthFilterDegraded)))
    };

    protected override async Task OnInitializedAsync()
    {
        BuildInstallCommand();
        await LoadSnapshotAsync();
    }

    public async Task LoadSnapshotAsync()
    {
        SnapshotLoading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            Snapshot = ProbeStore.ReadDashboardSnapshot();
            RecomputeDerivedState();
        }
        finally
        {
            SnapshotLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    public void ToggleCollapseAll()
    {
        CollapseAll = !CollapseAll;
        if (CollapseAll)
        {
            GroupOpen.Clear();
        }

        RecomputeDerivedState();
    }

    public void SetHealthFilter(string filter)
    {
        HealthFilter = filter;
    }

    public async Task OnSearchChanged(string value)
    {
        Search = value;
        await Task.CompletedTask;
    }

    public async Task OnGroupExpandedChanged((string GroupName, bool Expanded) args)
    {
        GroupOpen[args.GroupName] = args.Expanded;
        await InvokeAsync(StateHasChanged);
    }

    public async Task OnNodeExpandedChanged((NodeBroadcastSnapshot Node, bool Expanded) args)
    {
        var id = NodeIdentity(args.Node);
        NodeOpen[id] = args.Expanded;

        if (args.Expanded)
        {
            await EnsureNodeDetailLoadedAsync(args.Node);
        }

        await InvokeAsync(StateHasChanged);
    }

    public bool IsNodeOpen(NodeBroadcastSnapshot node) => NodeOpen.GetValueOrDefault(NodeIdentity(node));

    public bool IsGroupOpen(string groupName)
    {
        if (CollapseAll)
        {
            return false;
        }

        return !GroupOpen.TryGetValue(groupName, out var open) || open;
    }

    public string AssessNode(NodeBroadcastSnapshot node)
    {
        var runtime = node.CurrentMetrics.Runtime;
        var sanity = node.CurrentMetrics.Sanity;

        if (sanity.DiskReadOnlyPanic ||
            runtime.CpuUsedPercent >= 90 ||
            runtime.MemoryUsedPercent >= 90 ||
            runtime.DiskUsedPercent >= 95)
        {
            return HealthFilterDegraded;
        }

        if (runtime.CpuUsedPercent >= 70 ||
            runtime.MemoryUsedPercent >= 75 ||
            runtime.DiskUsedPercent >= 85)
        {
            return HealthFilterWarning;
        }

        return HealthFilterHealthy;
    }

    public string HealthLabel(string value) => value switch
    {
        HealthFilterHealthy => "Healthy",
        HealthFilterWarning => "Warning",
        HealthFilterDegraded => "Degraded",
        _ => "Unknown"
    };

    public Color HealthChipColor(string value) => value switch
    {
        HealthFilterHealthy => Color.Success,
        HealthFilterWarning => Color.Warning,
        HealthFilterDegraded => Color.Error,
        _ => Color.Default
    };

    public Variant FilterButtonVariant(string value) => HealthFilter == value ? Variant.Filled : Variant.Outlined;

    public ProbeRuntimeInfo CurrentRuntime(NodeBroadcastSnapshot node) => node.CurrentMetrics.Runtime;

    public string NodeIdentity(NodeBroadcastSnapshot node) => $"{node.Registration.RemoteIp}:{node.Registration.MetricsPort}";

    public string NodeName(NodeBroadcastSnapshot node) =>
        string.IsNullOrWhiteSpace(node.Registration.NodeName) ? "Unnamed node" : node.Registration.NodeName;

    public string NodeGroup(NodeBroadcastSnapshot node) =>
        string.IsNullOrWhiteSpace(node.Registration.GroupName) ? "Default" : node.Registration.GroupName;

    public int NodePort(NodeBroadcastSnapshot node) => node.Registration.MetricsPort;

    public string NodeLabel(NodeBroadcastSnapshot node) => $"{NodeName(node)} · {NodeGroup(node)} · {node.Registration.RemoteIp}:{NodePort(node)}";

    public string FormatTime(DateTimeOffset value) => value == default ? "-" : value.ToString("g");

    public string FormatPercent(double value) => $"{value:0.0}%";

    public string FormatNumber(double value, int digits) => value.ToString($"F{digits}");

    public string FormatBytes(long value)
    {
        var v = (double)value;
        return v switch
        {
            >= 1e12 => $"{v / 1e12:0.00} TB",
            >= 1e9 => $"{v / 1e9:0.00} GB",
            >= 1e6 => $"{v / 1e6:0.00} MB",
            >= 1e3 => $"{v / 1e3:0.00} KB",
            _ => $"{v:0} B"
        };
    }

    public string BuildSparkline(HistoricalSnapshot[] history)
    {
        if (history.Length == 0)
        {
            return string.Empty;
        }

        var values = history.Select(item => item.Runtime.NetRxSpeedKbps + item.Runtime.NetTxSpeedKbps).ToArray();
        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(1d, max - min);
        var width = 160d;
        var height = 56d;
        var padding = 4d;
        var step = values.Length == 1 ? 0 : (width - padding * 2) / (values.Length - 1);

        var points = values.Select((value, index) =>
        {
            var x = padding + step * index;
            var y = height - padding - ((value - min) / range) * (height - padding * 2);
            return $"{x:0.00},{y:0.00}";
        });

        return string.Join(" ", points);
    }

    public Task CopyInstallCommandAsync()
    {
        return CopyInstallCommandCoreAsync();
    }

    public async Task EnsureNodeDetailLoadedAsync(NodeBroadcastSnapshot node)
    {
        var id = NodeIdentity(node);
        if (NodeDetails.ContainsKey(id) || LoadingDetails.Contains(id))
        {
            return;
        }

        await LoadNodeDetailAsync(node);
    }

    private async Task CopyInstallCommandCoreAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ghostMon.copyText", InstallCommand);
            AgentInstallStatus = "Install command copied to clipboard.";
        }
        catch
        {
            AgentInstallStatus = "Copy failed, please select the install command manually.";
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadNodeDetailAsync(NodeBroadcastSnapshot node)
    {
        var id = NodeIdentity(node);
        if (LoadingDetails.Contains(id))
        {
            return;
        }

        LoadingDetails.Add(id);
        try
        {
            var reg = node.Registration;
            var detail = await ProbeStore.ReadNodeDetailAsync(reg.RemoteIp, reg.MetricsPort);
            if (detail is not null)
            {
                NodeDetails[id] = detail;
            }
        }
        finally
        {
            LoadingDetails.Remove(id);
            await InvokeAsync(StateHasChanged);
        }
    }

    private IReadOnlyList<NodeBroadcastSnapshot> GetVisibleNodes()
    {
        var query = Search.Trim();
        return (Snapshot?.Nodes ?? Array.Empty<NodeBroadcastSnapshot>())
            .Where(node =>
            {
                var healthMatches = HealthFilter == HealthFilterAll || HealthFilter == AssessNode(node);
                return healthMatches && MatchesSearch(node, query);
            })
            .OrderBy(node => NodeGroup(node), StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => NodeName(node), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<DashboardGroupView> GroupVisibleNodes()
    {
        return GetVisibleNodes()
            .GroupBy(node => NodeGroup(node))
            .Select(group => new DashboardGroupView(
                group.Key,
                group.OrderBy(node => NodeName(node), StringComparer.OrdinalIgnoreCase).ToArray(),
                group.Count(node => AssessNode(node) == HealthFilterHealthy),
                group.Count(node => AssessNode(node) == HealthFilterWarning),
                group.Count(node => AssessNode(node) == HealthFilterDegraded)))
            .OrderBy(group => group.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool MatchesSearch(NodeBroadcastSnapshot node, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var reg = node.Registration;
        var metrics = node.CurrentMetrics;
        var assets = metrics.Assets ?? reg.Assets;
        var haystack = string.Join(" ", new[]
        {
            reg.NodeName,
            reg.GroupName,
            assets?.CpuModelName,
            assets?.OsPlatform,
            reg.RemoteIp,
            reg.MetricsPort.ToString()
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RecomputeDerivedState()
    {
        var visibleNodes = GetVisibleNodes();
        var summary = SummarizeHealth(visibleNodes);
        ClusterValue = summary.Label;
        ClusterDetail = summary.Detail;
        HealthValue = summary.Label;
        HealthDetail = summary.Detail;
        HealthColor = summary.Color;

        var totalRx = 0d;
        var totalTx = 0d;
        foreach (var node in visibleNodes)
        {
            totalRx += CurrentRuntime(node).NetRxSpeedKbps;
            totalTx += CurrentRuntime(node).NetTxSpeedKbps;
        }

        TrafficValue = $"{FormatNumber(totalRx + totalTx, 1)} kbps";
        TrafficDetail = $"Rx {FormatNumber(totalRx, 1)} kbps / Tx {FormatNumber(totalTx, 1)} kbps";
        FilterSummary = BuildFilterSummary();
        ToggleGroupsText = CollapseAll ? "Expand all" : "Collapse all";
        AgentInstallStatus = $"Install command generated from {RuntimeSettings.AgentImage} and the current page URL.";
    }

    private string BuildFilterSummary()
    {
        var active = new List<string>();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            active.Add($"Search=\"{Search}\"");
        }

        if (HealthFilter != HealthFilterAll)
        {
            active.Add($"Health={HealthFilter}");
        }

        return active.Count == 0 ? "No filter is active." : string.Join(" · ", active);
    }

    private HealthSummary SummarizeHealth(IReadOnlyList<NodeBroadcastSnapshot> nodes)
    {
        if (nodes.Count == 0)
        {
            return new HealthSummary("N/A", "No nodes have reported yet.", Color.Default);
        }

        var degraded = nodes.Count(node => AssessNode(node) == HealthFilterDegraded);
        var warning = nodes.Count(node => AssessNode(node) == HealthFilterWarning);

        if (degraded > 0)
        {
            return new HealthSummary("Degraded", $"{degraded} nodes are degraded, {warning} more are warning.", Color.Error);
        }

        if (warning > 0)
        {
            return new HealthSummary("Warning", $"{warning} nodes need attention.", Color.Warning);
        }

        return new HealthSummary("Healthy", "No active warnings in the current snapshot.", Color.Success);
    }

    private void BuildInstallCommand()
    {
        var dashboardBaseUrl = NavigationManager.BaseUri.TrimEnd('/');
        InstallCommand = string.Join(Environment.NewLine, new[]
        {
            "docker run -d \\",
            "  --name ghostmon-agent \\",
            "  --restart unless-stopped \\",
            "  --add-host=host.docker.internal:host-gateway \\",
            $"  -e DashboardBaseUrl={QuoteShell(dashboardBaseUrl)} \\",
            $"  -e SecurityToken={QuoteShell(RuntimeSettings.SecurityToken)} \\",
            "  -e NodeName=node-01 \\",
            "  -e GroupName=default \\",
            "  -e AgentPort=8081 \\",
            $"  -e TelemetryIntervalSeconds={RuntimeSettings.TelemetryIntervalSeconds} \\",
            $"  -e PingTimeoutMilliseconds={RuntimeSettings.PingTimeoutMilliseconds} \\",
            $"  -e PingTargetMode={RuntimeSettings.PingTargetMode} \\",
            $"  -e PingTargets={QuoteShell(string.Join(",", RuntimeSettings.PingTargets))} \\",
            "  -e HostProcPath=/host-proc \\",
            "  -e HostSysPath=/host-sys \\",
            "  -e HostRootPath=/host-root \\",
            "  -e HostTmpPath=/host-tmp \\",
            $"  {QuoteShell(RuntimeSettings.AgentImage)}"
        });
    }

    private static string QuoteShell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        if (!value.Contains('\''))
        {
            return $"'{value}'";
        }

        return "'" + value.Replace("'", "'\\''") + "'";
    }

    private sealed record HealthSummary(string Label, string Detail, Color Color);
}
