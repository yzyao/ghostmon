using System.Net;
using System.Net.NetworkInformation;
using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentMetricsService
{
    private readonly AgentRuntimeSettings _settings;
    private readonly AgentRuntimeState _state;
    private readonly object _gate = new();
    private ProbeCounters _previousCounters;

    public AgentMetricsService(AgentRuntimeSettings settings, AgentRuntimeState state)
    {
        _settings = settings;
        _state = state;
        Assets = AgentHostMetricsReader.DiscoverAssets(settings);
        _previousCounters = AgentHostMetricsReader.ReadCurrentCounters(settings);
    }

    public ProbeAssetsInfo Assets { get; }

    public async Task<ProbeMetrics> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var runtime = SampleRuntime();
        var sanity = new ProbeSanityInfo
        {
            DiskReadOnlyPanic = AgentHostMetricsReader.DetectDiskReadOnlyPanic(_settings)
        };

        var snapshot = _state.Snapshot;
        var pingPlan = ResolvePingPlan(snapshot);
        var timeout = TimeSpan.FromMilliseconds(snapshot.PingTimeoutMilliseconds);

        var pingV4Task = CreatePingTask(pingPlan.V4Target, timeout, cancellationToken);
        var pingV6Task = CreatePingTask(pingPlan.V6Target, timeout, cancellationToken);

        await Task.WhenAll(pingV4Task, pingV6Task);

        return new ProbeMetrics
        {
            Assets = _state.ShouldIncludeAssets() ? Assets : null,
            Runtime = runtime with
            {
                PingV4DelayMs = pingV4Task.Result,
                PingV6DelayMs = pingV6Task.Result
            },
            Sanity = sanity
        };
    }

    private ProbeRuntimeInfo SampleRuntime()
    {
        lock (_gate)
        {
            var current = AgentHostMetricsReader.ReadCurrentCounters(_settings);
            var elapsedMs = Math.Max(1, current.TickMs - _previousCounters.TickMs);
            var runtime = AgentHostMetricsReader.CreateRuntimeInfo(_previousCounters, current, elapsedMs, _settings);
            _previousCounters = current;
            return runtime;
        }
    }

    private static PingPlan ResolvePingPlan(AgentRuntimeState.RuntimeSnapshot snapshot)
    {
        if (snapshot.PingTargets.Length == 0)
        {
            return default;
        }

        IPAddress? v4Target = null;
        IPAddress? v6Target = null;

        foreach (var value in snapshot.PingTargets)
        {
            if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value.Trim(), out var target))
            {
                continue;
            }

            if (target.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && v4Target is null)
            {
                v4Target = target;
                continue;
            }

            if (target.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && v6Target is null)
            {
                v6Target = target;
            }
        }

        return snapshot.PingTargetMode switch
        {
            PingTargetMode.V4 => v4Target is null ? default : new PingPlan(v4Target, null),
            PingTargetMode.V6 => v6Target is null ? default : new PingPlan(null, v6Target),
            _ => new PingPlan(v4Target, v6Target)
        };
    }

    private static Task<int?> CreatePingTask(IPAddress? target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return target is null
            ? Task.FromResult<int?>(null)
            : RetryAsync(() => MeasurePingAsync(target, timeout, cancellationToken), cancellationToken);
    }

    private static async Task<int?> RetryAsync(Func<Task<int?>> action, CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        var delayMs = 100;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await action();
            if (result.HasValue)
            {
                return result;
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        return null;
    }

    private static async Task<int?> MeasurePingAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, (int)Math.Max(1, timeout.TotalMilliseconds));
            if (reply.Status == IPStatus.Success)
            {
                return reply.RoundtripTime < 0 ? 0 : (int)Math.Min(reply.RoundtripTime, int.MaxValue);
            }
        }
        catch
        {
        }

        return null;
    }

    private readonly record struct PingPlan(IPAddress? V4Target, IPAddress? V6Target);
}
