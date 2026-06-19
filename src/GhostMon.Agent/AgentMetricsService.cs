using System.Net;
using System.Net.Sockets;
using GhostMon.Contracts;

namespace GhostMon.Agent;

public sealed class AgentMetricsService
{
    private static readonly IPAddress PublicProbeIpv4 = IPAddress.Parse("1.1.1.1");
    private static readonly IPAddress PublicProbeIpv6 = IPAddress.Parse("2606:4700:4700::1111");

    private readonly HttpClient _probeClient;
    private readonly object _gate = new();
    private ProbeCounters _previousCounters;

    public AgentMetricsService(IHttpClientFactory httpClientFactory)
    {
        _probeClient = httpClientFactory.CreateClient("self-probe");
        Assets = DiscoverAssets();
        _previousCounters = ReadCurrentCounters();
    }

    public ProbeAssetsInfo Assets { get; }

    public async Task<ProbeMetrics> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var runtime = SampleRuntime();
        var diskReadOnlyPanic = DetectDiskReadOnlyPanic();

        var publicIpv4Task = ResolvePublicIpAsync("https://api.ipify.org?format=text", cancellationToken);
        var publicIpv6Task = ResolvePublicIpAsync("https://api6.ipify.org?format=text", cancellationToken);
        var chatGptTask = ProbeChatGptAsync(cancellationToken);
        var pingV4Task = MeasureTcpLatencyAsync(PublicProbeIpv4, 443, cancellationToken);
        var pingV6Task = MeasureTcpLatencyAsync(PublicProbeIpv6, 443, cancellationToken);

        await Task.WhenAll(publicIpv4Task, publicIpv6Task, chatGptTask, pingV4Task, pingV6Task);

        return new ProbeMetrics
        {
            Assets = Assets,
            Runtime = runtime with
            {
                PingV4DelayMs = pingV4Task.Result,
                PingV6DelayMs = pingV6Task.Result
            },
            Sanity = new ProbeSanityInfo
            {
                PublicIPv4 = publicIpv4Task.Result,
                PublicIPv6 = publicIpv6Task.Result,
                DiskReadOnlyPanic = diskReadOnlyPanic,
                ChatGPTUnlocked = chatGptTask.Result
            }
        };
    }

    private ProbeRuntimeInfo SampleRuntime()
    {
        lock (_gate)
        {
            var current = ReadCurrentCounters();
            var elapsedMs = Math.Max(1, current.TickMs - _previousCounters.TickMs);
            var runtime = CreateRuntimeInfo(_previousCounters, current, elapsedMs);
            _previousCounters = current;
            return runtime;
        }
    }

    private static ProbeRuntimeInfo CreateRuntimeInfo(ProbeCounters previous, ProbeCounters current, long elapsedMs)
    {
        var elapsedSeconds = elapsedMs / 1000d;

        var cpuTotalDelta = current.CpuTotal - previous.CpuTotal;
        var cpuIdleDelta = current.CpuIdle - previous.CpuIdle;
        var cpuUsedPercent = 0d;
        if (cpuTotalDelta > 0)
        {
            var busyDelta = Math.Max(0, cpuTotalDelta - Math.Max(0, cpuIdleDelta));
            cpuUsedPercent = Math.Clamp((busyDelta * 100d) / cpuTotalDelta, 0d, 100d);
        }

        var netRxDelta = Math.Max(0, current.NetTotalRxBytes - previous.NetTotalRxBytes);
        var netTxDelta = Math.Max(0, current.NetTotalTxBytes - previous.NetTotalTxBytes);
        var diskReadDelta = Math.Max(0, current.DiskReadSectors - previous.DiskReadSectors);
        var diskWriteDelta = Math.Max(0, current.DiskWriteSectors - previous.DiskWriteSectors);

        return new ProbeRuntimeInfo
        {
            UpTimeSeconds = ReadUptimeSeconds(),
            CpuUsedPercent = cpuUsedPercent,
            MemoryUsedPercent = ReadMemoryUsedPercent(),
            SwapUsedPercent = ReadSwapUsedPercent(),
            DiskUsedPercent = ReadDiskUsedPercent(),
            DiskReadBytesPerSec = elapsedSeconds > 0 ? (long)Math.Round((diskReadDelta * 512d) / elapsedSeconds) : 0,
            DiskWriteBytesPerSec = elapsedSeconds > 0 ? (long)Math.Round((diskWriteDelta * 512d) / elapsedSeconds) : 0,
            NetRxSpeedKbps = elapsedSeconds > 0 ? (netRxDelta * 8d) / elapsedSeconds / 1000d : 0,
            NetTxSpeedKbps = elapsedSeconds > 0 ? (netTxDelta * 8d) / elapsedSeconds / 1000d : 0,
            BootTotalRxBytes = current.NetTotalRxBytes,
            BootTotalTxBytes = current.NetTotalTxBytes
        };
    }

    private static ProbeAssetsInfo DiscoverAssets()
    {
        return new ProbeAssetsInfo
        {
            CpuModelName = ReadCpuModelName(),
            OsPlatform = "Linux (x64)",
            MemoryTotalMb = ClampToInt(ReadMemInfoKb("MemTotal") / 1024),
            SwapTotalMb = ClampToInt(ReadMemInfoKb("SwapTotal") / 1024),
            DiskTotalGb = ClampToInt(ReadRootDiskTotalBytes() / 1024 / 1024 / 1024)
        };
    }

    private static ProbeCounters ReadCurrentCounters()
    {
        var cpu = ReadCpuCounters();
        var network = ReadNetworkTotals();
        var disk = ReadDiskTotals();
        return new ProbeCounters(
            TickMs: Environment.TickCount64,
            CpuTotal: cpu.Total,
            CpuIdle: cpu.Idle,
            NetTotalRxBytes: network.RxBytes,
            NetTotalTxBytes: network.TxBytes,
            DiskReadSectors: disk.ReadSectors,
            DiskWriteSectors: disk.WriteSectors);
    }

    private static (long Total, long Idle) ReadCpuCounters()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (!line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    break;
                }

                long user = ParseLong(parts, 1);
                long nice = ParseLong(parts, 2);
                long system = ParseLong(parts, 3);
                long idle = ParseLong(parts, 4);
                long iowait = parts.Length > 5 ? ParseLong(parts, 5) : 0;
                long irq = parts.Length > 6 ? ParseLong(parts, 6) : 0;
                long softirq = parts.Length > 7 ? ParseLong(parts, 7) : 0;
                long steal = parts.Length > 8 ? ParseLong(parts, 8) : 0;
                long guest = parts.Length > 9 ? ParseLong(parts, 9) : 0;
                long guestNice = parts.Length > 10 ? ParseLong(parts, 10) : 0;

                var total = user + nice + system + idle + iowait + irq + softirq + steal + guest + guestNice;
                return (total, idle + iowait);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private static (long RxBytes, long TxBytes) ReadNetworkTotals()
    {
        long rxBytes = 0;
        long txBytes = 0;

        try
        {
            foreach (var line in File.ReadLines("/proc/net/dev"))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue;
                }

                var iface = line[..colonIndex].Trim();
                if (string.Equals(iface, "lo", StringComparison.Ordinal))
                {
                    continue;
                }

                var fields = line[(colonIndex + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 16)
                {
                    continue;
                }

                rxBytes += ParseLong(fields, 0);
                txBytes += ParseLong(fields, 8);
            }
        }
        catch
        {
        }

        return (rxBytes, txBytes);
    }

    private static (long ReadSectors, long WriteSectors) ReadDiskTotals()
    {
        long readSectors = 0;
        long writeSectors = 0;

        try
        {
            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10)
                {
                    continue;
                }

                var device = parts[2];
                if (!Directory.Exists(Path.Combine("/sys/block", device)))
                {
                    continue;
                }

                if (device.StartsWith("loop", StringComparison.Ordinal) ||
                    device.StartsWith("ram", StringComparison.Ordinal))
                {
                    continue;
                }

                readSectors += ParseLong(parts, 5);
                writeSectors += ParseLong(parts, 9);
            }
        }
        catch
        {
        }

        return (readSectors, writeSectors);
    }

    private static string ReadCpuModelName()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (!line.StartsWith("model name", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex >= 0 && separatorIndex + 1 < line.Length)
                {
                    var model = line[(separatorIndex + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(model))
                    {
                        return model;
                    }
                }
            }
        }
        catch
        {
        }

        return "Unknown CPU";
    }

    private static long ReadMemInfoKb(string key)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith(key, StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var raw = line[(separatorIndex + 1)..].Trim();
                var spaceIndex = raw.IndexOf(' ');
                var number = spaceIndex >= 0 ? raw[..spaceIndex] : raw;
                if (long.TryParse(number, out var value))
                {
                    return value;
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private static long ReadRootDiskTotalBytes()
    {
        try
        {
            var rootDrive = new DriveInfo("/");
            if (rootDrive.IsReady)
            {
                return rootDrive.TotalSize;
            }
        }
        catch
        {
        }

        return 0;
    }

    private static long ReadUptimeSeconds()
    {
        try
        {
            var line = File.ReadLines("/proc/uptime").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line))
            {
                var token = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                if (double.TryParse(token, out var seconds))
                {
                    return (long)Math.Max(0, Math.Floor(seconds));
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private static double ReadMemoryUsedPercent()
    {
        var total = ReadMemInfoKb("MemTotal");
        if (total <= 0)
        {
            return 0;
        }

        var available = ReadMemInfoKb("MemAvailable");
        if (available <= 0)
        {
            available = ReadMemInfoKb("MemFree");
        }

        var used = Math.Max(0, total - available);
        return Math.Clamp((used * 100d) / total, 0d, 100d);
    }

    private static double ReadSwapUsedPercent()
    {
        var total = ReadMemInfoKb("SwapTotal");
        if (total <= 0)
        {
            return 0;
        }

        var free = ReadMemInfoKb("SwapFree");
        var used = Math.Max(0, total - free);
        return Math.Clamp((used * 100d) / total, 0d, 100d);
    }

    private static double ReadDiskUsedPercent()
    {
        try
        {
            var drive = new DriveInfo("/");
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return 0;
            }

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return Math.Clamp((used * 100d) / drive.TotalSize, 0d, 100d);
        }
        catch
        {
            return 0;
        }
    }

    private static bool DetectDiskReadOnlyPanic()
    {
        const string tempDir = "/tmp";

        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, $"ghostmon-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough | FileOptions.DeleteOnClose);

            stream.WriteByte(0x5A);
            stream.Flush();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<string?> ResolvePublicIpAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _probeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ProbeChatGptAsync(CancellationToken cancellationToken)
    {
        return await CanReachAsync("https://chatgpt.com/", cancellationToken) ||
               await CanReachAsync("https://openai.com/", cancellationToken);
    }

    private async Task<bool> CanReachAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("GhostMon/1.0");
            using var response = await _probeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int?> MeasureTcpLatencyAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            var start = Environment.TickCount64;
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            var elapsed = Environment.TickCount64 - start;
            return elapsed < 0 ? 0 : (int)Math.Min(elapsed, int.MaxValue);
        }
        catch
        {
            return null;
        }
    }

    private static int ClampToInt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static long ParseLong(string[] parts, int index)
    {
        return index >= 0 && index < parts.Length && long.TryParse(parts[index], out var value) ? value : 0;
    }

    private readonly record struct ProbeCounters(
        long TickMs,
        long CpuTotal,
        long CpuIdle,
        long NetTotalRxBytes,
        long NetTotalTxBytes,
        long DiskReadSectors,
        long DiskWriteSectors);
}