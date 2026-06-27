using GhostMon.Contracts;

namespace GhostMon.Agent;

internal static class AgentHostMetricsReader
{
    public static ProbeAssetsInfo DiscoverAssets(AgentRuntimeSettings settings)
    {
        var memInfo = ReadMemInfoSnapshot(settings);
        var rootDisk = ReadRootDiskSnapshot(settings);

        return new ProbeAssetsInfo
        {
            CpuModelName = ReadCpuModelName(settings),
            OsPlatform = "Linux (x64)",
            MemoryTotalMb = ClampToInt(memInfo.MemTotalKb / 1024),
            SwapTotalMb = ClampToInt(memInfo.SwapTotalKb / 1024),
            DiskTotalGb = ClampToInt(rootDisk.TotalSizeBytes / 1024 / 1024 / 1024)
        };
    }

    public static ProbeRuntimeInfo CreateRuntimeInfo(ProbeCounters previous, ProbeCounters current, long elapsedMs, AgentRuntimeSettings settings)
    {
        var elapsedSeconds = elapsedMs / 1000d;
        var memInfo = ReadMemInfoSnapshot(settings);
        var rootDisk = ReadRootDiskSnapshot(settings);

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
            UpTimeSeconds = ReadUptimeSeconds(settings),
            CpuUsedPercent = cpuUsedPercent,
            MemoryUsedPercent = ReadMemoryUsedPercent(memInfo),
            SwapUsedPercent = ReadSwapUsedPercent(memInfo),
            DiskUsedPercent = ReadDiskUsedPercent(rootDisk),
            DiskReadBytesPerSec = elapsedSeconds > 0 ? (long)Math.Round((diskReadDelta * 512d) / elapsedSeconds) : 0,
            DiskWriteBytesPerSec = elapsedSeconds > 0 ? (long)Math.Round((diskWriteDelta * 512d) / elapsedSeconds) : 0,
            NetRxSpeedKbps = elapsedSeconds > 0 ? (netRxDelta * 8d) / elapsedSeconds / 1000d : 0,
            NetTxSpeedKbps = elapsedSeconds > 0 ? (netTxDelta * 8d) / elapsedSeconds / 1000d : 0,
            BootTotalRxBytes = current.NetTotalRxBytes,
            BootTotalTxBytes = current.NetTotalTxBytes
        };
    }

    public static ProbeCounters ReadCurrentCounters(AgentRuntimeSettings settings)
    {
        var cpu = ReadCpuCounters(settings);
        var network = ReadNetworkTotals(settings);
        var disk = ReadDiskTotals(settings);
        return new ProbeCounters(
            TickMs: Environment.TickCount64,
            CpuTotal: cpu.Total,
            CpuIdle: cpu.Idle,
            NetTotalRxBytes: network.RxBytes,
            NetTotalTxBytes: network.TxBytes,
            DiskReadSectors: disk.ReadSectors,
            DiskWriteSectors: disk.WriteSectors);
    }

    public static bool DetectDiskReadOnlyPanic(AgentRuntimeSettings settings)
    {
        try
        {
            Directory.CreateDirectory(settings.HostTmpPath);
            var path = Path.Combine(settings.HostTmpPath, $"ghostmon-{Guid.NewGuid():N}.tmp");
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

    private static (long Total, long Idle) ReadCpuCounters(AgentRuntimeSettings settings)
    {
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(settings.HostProcPath, "stat")))
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

                var user = ParseLong(parts, 1);
                var nice = ParseLong(parts, 2);
                var system = ParseLong(parts, 3);
                var idle = ParseLong(parts, 4);
                var iowait = parts.Length > 5 ? ParseLong(parts, 5) : 0;
                var irq = parts.Length > 6 ? ParseLong(parts, 6) : 0;
                var softirq = parts.Length > 7 ? ParseLong(parts, 7) : 0;
                var steal = parts.Length > 8 ? ParseLong(parts, 8) : 0;
                var guest = parts.Length > 9 ? ParseLong(parts, 9) : 0;
                var guestNice = parts.Length > 10 ? ParseLong(parts, 10) : 0;

                var total = user + nice + system + idle + iowait + irq + softirq + steal + guest + guestNice;
                return (total, idle + iowait);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private static (long RxBytes, long TxBytes) ReadNetworkTotals(AgentRuntimeSettings settings)
    {
        long rxBytes = 0;
        long txBytes = 0;

        try
        {
            foreach (var line in File.ReadLines(Path.Combine(settings.HostProcPath, "net", "dev")))
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

    private static (long ReadSectors, long WriteSectors) ReadDiskTotals(AgentRuntimeSettings settings)
    {
        long readSectors = 0;
        long writeSectors = 0;

        try
        {
            foreach (var line in File.ReadLines(Path.Combine(settings.HostProcPath, "diskstats")))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10)
                {
                    continue;
                }

                var device = parts[2];
                if (!Directory.Exists(Path.Combine(settings.HostSysPath, "block", device)))
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

    private static string ReadCpuModelName(AgentRuntimeSettings settings)
    {
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(settings.HostProcPath, "cpuinfo")))
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

    private static MemInfoSnapshot ReadMemInfoSnapshot(AgentRuntimeSettings settings)
    {
        long memTotalKb = 0;
        long memAvailableKb = 0;
        long memFreeKb = 0;
        long swapTotalKb = 0;
        long swapFreeKb = 0;

        try
        {
            foreach (var line in File.ReadLines(Path.Combine(settings.HostProcPath, "meminfo")))
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex];
                var raw = line[(separatorIndex + 1)..].Trim();
                var spaceIndex = raw.IndexOf(' ');
                var number = spaceIndex >= 0 ? raw[..spaceIndex] : raw;
                if (!long.TryParse(number, out var value))
                {
                    continue;
                }

                switch (key)
                {
                    case "MemTotal":
                        memTotalKb = value;
                        break;
                    case "MemAvailable":
                        memAvailableKb = value;
                        break;
                    case "MemFree":
                        memFreeKb = value;
                        break;
                    case "SwapTotal":
                        swapTotalKb = value;
                        break;
                    case "SwapFree":
                        swapFreeKb = value;
                        break;
                }
            }
        }
        catch
        {
        }

        return new MemInfoSnapshot(memTotalKb, memAvailableKb, memFreeKb, swapTotalKb, swapFreeKb);
    }

    private static RootDiskSnapshot ReadRootDiskSnapshot(AgentRuntimeSettings settings)
    {
        try
        {
            var rootDrive = new DriveInfo(settings.HostRootPath);
            if (rootDrive.IsReady)
            {
                return new RootDiskSnapshot(rootDrive.TotalSize, rootDrive.AvailableFreeSpace);
            }
        }
        catch
        {
        }

        return default;
    }

    private static long ReadUptimeSeconds(AgentRuntimeSettings settings)
    {
        try
        {
            var line = File.ReadLines(Path.Combine(settings.HostProcPath, "uptime")).FirstOrDefault();
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

    private static double ReadMemoryUsedPercent(MemInfoSnapshot memInfo)
    {
        if (memInfo.MemTotalKb <= 0)
        {
            return 0;
        }

        var available = memInfo.MemAvailableKb > 0 ? memInfo.MemAvailableKb : memInfo.MemFreeKb;

        var used = Math.Max(0, memInfo.MemTotalKb - available);
        return Math.Clamp((used * 100d) / memInfo.MemTotalKb, 0d, 100d);
    }

    private static double ReadSwapUsedPercent(MemInfoSnapshot memInfo)
    {
        if (memInfo.SwapTotalKb <= 0)
        {
            return 0;
        }

        var used = Math.Max(0, memInfo.SwapTotalKb - memInfo.SwapFreeKb);
        return Math.Clamp((used * 100d) / memInfo.SwapTotalKb, 0d, 100d);
    }

    private static double ReadDiskUsedPercent(RootDiskSnapshot rootDisk)
    {
        if (!rootDisk.IsReady || rootDisk.TotalSizeBytes <= 0)
        {
            return 0;
        }

        var used = rootDisk.TotalSizeBytes - rootDisk.AvailableFreeSpaceBytes;
        return Math.Clamp((used * 100d) / rootDisk.TotalSizeBytes, 0d, 100d);
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

    private readonly record struct MemInfoSnapshot(
        long MemTotalKb,
        long MemAvailableKb,
        long MemFreeKb,
        long SwapTotalKb,
        long SwapFreeKb);

    private readonly record struct RootDiskSnapshot(long TotalSizeBytes, long AvailableFreeSpaceBytes)
    {
        public bool IsReady => TotalSizeBytes > 0;
    }
}

internal readonly record struct ProbeCounters(
    long TickMs,
    long CpuTotal,
    long CpuIdle,
    long NetTotalRxBytes,
    long NetTotalTxBytes,
    long DiskReadSectors,
    long DiskWriteSectors);
