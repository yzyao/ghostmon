using System.Net;
using System.Net.NetworkInformation;
using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentMetricsService
{
    private static readonly IPAddress FallbackV4Target = IPAddress.Parse("1.1.1.1");
    private static readonly IPAddress FallbackV6Target = IPAddress.Parse("2606:4700:4700::1111");

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

        var pingV4Task = pingPlan.V4Target is null
            ? Task.FromResult<int?>(null)
            : RetryAsync(() => MeasurePingAsync(pingPlan.V4Target, timeout, cancellationToken), cancellationToken);

        var pingV6Task = pingPlan.V6Target is null
            ? Task.FromResult<int?>(null)
            : RetryAsync(() => MeasurePingAsync(pingPlan.V6Target, timeout, cancellationToken), cancellationToken);

        await Task.WhenAll(pingV4Task, pingV6Task);

        return new ProbeMetrics
        {
            Assets = Assets,
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
        var explicitTargets = snapshot.PingTargets
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Select(value => IPAddress.TryParse(value, out var parsed) ? parsed : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        IPAddress? v4Target = null;
        IPAddress? v6Target = null;

        foreach (var target in explicitTargets)
        {
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
            PingTargetMode.V4 => new PingPlan(v4Target ?? FallbackV4Target, null),
            PingTargetMode.V6 => new PingPlan(null, v6Target ?? FallbackV6Target),
            _ => new PingPlan(v4Target ?? FallbackV4Target, v6Target ?? FallbackV6Target)
        };
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
