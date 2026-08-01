using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.Metrics;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Adapters;
using GpuPreferenceManager.Windows.Diagnostics;
using GpuPreferenceManager.Windows.Metrics;
using GpuPreferenceManager.Windows.Processes;
using GpuPreferenceManager.Windows.Registry;
using Serilog;
using Serilog.Core;

return await GpuProbeProgram.RunAsync(args);

internal static class GpuProbeProgram
{
    private static readonly JsonSerializerOptions InventoryJsonOptions = CreateJsonOptions();

    public static async Task<int> RunAsync(string[] args)
    {
        using Logger logger = DiagnosticLogging.CreateLogger("gpu-probe");
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "registry" => await PrintRegistryAsync(CancellationToken.None),
                "adapters" => await PrintAdaptersAsync(CancellationToken.None),
                "sample" => await PrintSamplesAsync(ParseSeconds(args), CancellationToken.None),
                "inventory" => await PrintInventoryAsync(ParseSeconds(args), CancellationToken.None),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception exception)
        {
            logger.Error(exception, "GpuProbe 命令 {Command} 执行失败", args[0]);
            Console.Error.WriteLine($"错误：{exception}");
            Console.Error.WriteLine("详细信息已写入本地诊断日志。");
            return 1;
        }
    }

    private static async Task<int> PrintInventoryAsync(int seconds, CancellationToken cancellationToken)
    {
        DxgiGpuAdapterEnumerator adapterEnumerator = new();
        IReadOnlyList<GpuAdapterDescriptor> adapters = adapterEnumerator.EnumerateAdapters();
        GpuMetricsSnapshot? latest = null;
        await using (PdhGpuMetricsSampler sampler = new())
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                await foreach (GpuMetricsSnapshot snapshot in sampler.SampleAsync(TimeSpan.FromSeconds(2), timeout.Token))
                {
                    latest = snapshot;
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
            }
        }

        if (latest is null)
        {
            Console.WriteLine("[]");
            return 0;
        }

        WindowsProcessInfoProvider processProvider = new();
        int[] processIds = latest.Values.Select(static value => value.Instance.ProcessId).Distinct().ToArray();
        IReadOnlyList<ProcessInfoSnapshot> allProcesses = await processProvider.GetAllAsync(cancellationToken);
        List<ProcessInfoSnapshot> processList = allProcesses.ToList();
        HashSet<int> knownProcessIds = processList.Select(static process => process.Key.ProcessId).ToHashSet();
        int[] missingActiveProcessIds = processIds.Where(processId => !knownProcessIds.Contains(processId)).ToArray();
        if (missingActiveProcessIds.Length > 0)
        {
            using SemaphoreSlim concurrency = new(8);
            ProcessInfoSnapshot[] missingProcesses = await Task.WhenAll(missingActiveProcessIds.Select(async processId =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    return await processProvider.GetAsync(processId, cancellationToken);
                }
                finally
                {
                    concurrency.Release();
                }
            }));
            processList.AddRange(missingProcesses);
        }

        ProcessInfoSnapshot[] processSnapshots = processList.ToArray();
        Dictionary<int, ProcessInstanceKey> processKeys = processSnapshots.ToDictionary(
            static process => process.Key.ProcessId,
            static process => process.Key);
        IReadOnlyList<ProcessAdapterGpuUsage> usage = GpuUsageAggregator.Aggregate(
            latest,
            processKeys,
            adapters,
            new GpuLuidMapper());
        Dictionary<ProcessInstanceKey, ProcessInfoSnapshot> processByKey = processSnapshots.ToDictionary(static item => item.Key);
        Dictionary<string, long> currentDedicated = usage
            .Where(item => processByKey.TryGetValue(item.Process, out ProcessInfoSnapshot? process)
                && ExecutablePathNormalizer.Normalize(process.ExecutablePath) is not null)
            .GroupBy(item => ExecutablePathNormalizer.Normalize(processByKey[item.Process].ExecutablePath)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(static item => item.DedicatedBytes),
                StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, long> peaks = new GpuUsagePeakTracker().Update(latest.SampleTimeUtc, currentDedicated);
        RegistrySnapshot registry = await new WindowsUserGpuPreferencesReader().ReadSnapshotAsync(cancellationToken);
        IReadOnlyList<ExecutableGpuUsage> inventory = ApplicationInventoryBuilder.Build(
            processSnapshots,
            usage,
            registry,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            peaks);
        var output = inventory.Select(item => new
        {
            item.ExecutablePath,
            item.DisplayName,
            item.Processes,
            AdapterUsages = item.AdapterUsages.Select(pair => new
            {
                Adapter = pair.Key,
                pair.Value.DedicatedBytes,
                pair.Value.SharedBytes,
                pair.Value.EngineUtilization,
            }),
            Rule = item.Rule.Kind,
            item.Rule.SpecificAdapterKey,
            item.Category,
            item.PeakDedicatedBytes30Seconds,
            item.LastSeenUtc,
        });
        Console.WriteLine(JsonSerializer.Serialize(output, InventoryJsonOptions));
        return 0;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<int> PrintSamplesAsync(int seconds, CancellationToken cancellationToken)
    {
        await using PdhGpuMetricsSampler sampler = new();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            await foreach (var snapshot in sampler.SampleAsync(TimeSpan.FromSeconds(2), timeout.Token))
            {
                Console.WriteLine($"Sample UTC: {snapshot.SampleTimeUtc:O}, values={snapshot.Values.Count}, unparsed={snapshot.UnparsedInstances.Count}");
                foreach (var value in snapshot.Values.OrderByDescending(static value => value.Value))
                {
                    string unit = value.Kind == GpuCounterKind.EngineUtilization ? "%" : " bytes";
                    Console.WriteLine($"  PID {value.Instance.ProcessId} LUID 0x{value.Instance.FirstLuidPart:X8}:0x{value.Instance.SecondLuidPart:X8} phys {value.Instance.PhysicalAdapterIndex} {value.Kind}={value.Value:F2}{unit} engine={value.Instance.EngineType ?? "-"}");
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }

        return 0;
    }

    private static int ParseSeconds(string[] args)
    {
        if (args.Length == 1)
        {
            return 30;
        }

        if (args.Length == 3
            && string.Equals(args[1], "--seconds", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            && seconds is >= 1 and <= 3600)
        {
            return seconds;
        }

        throw new ArgumentException("sample 用法：GpuProbe sample [--seconds 1..3600]");
    }

    private static async Task<int> PrintRegistryAsync(CancellationToken cancellationToken)
    {
        WindowsUserGpuPreferencesReader reader = new();
        RegistrySnapshot snapshot = await reader.ReadSnapshotAsync(cancellationToken);
        Console.WriteLine($"UserGpuPreferences key: {(snapshot.KeyExists ? "存在" : "不存在")}");
        Console.WriteLine($"Captured UTC: {snapshot.CapturedAtUtc:O}");

        if (!snapshot.KeyExists)
        {
            return 0;
        }

        if (snapshot.GlobalSettings is not null)
        {
            Console.WriteLine();
            Console.WriteLine("DirectXUserGlobalSettings（全局设置，不是应用规则）");
            Console.WriteLine($"  HighPerfAdapter: {snapshot.GlobalSettings.HighPerformanceAdapterKey ?? "<未设置>"}");
            Console.WriteLine($"  Raw: {snapshot.GlobalSettings.RawValue}");
        }

        foreach (RegistryValueSnapshot value in snapshot.ApplicationValues)
        {
            Console.WriteLine();
            Console.WriteLine(value.Name);
            Console.WriteLine($"  Type: {value.Data.Kind}");
            Console.WriteLine($"  Classification: {value.Rule?.Kind.ToString() ?? "NotStringRule"}");
            if (value.Rule?.SpecificAdapterKey is not null)
            {
                Console.WriteLine($"  SpecificAdapter: {value.Rule.SpecificAdapterKey}");
            }

            Console.WriteLine($"  Raw: {value.Data.StringValue ?? "<非字符串>"}");
        }

        return 0;
    }

    private static async Task<int> PrintAdaptersAsync(CancellationToken cancellationToken)
    {
        WindowsUserGpuPreferencesReader reader = new();
        RegistrySnapshot snapshot = await reader.ReadSnapshotAsync(cancellationToken);
        DxgiGpuAdapterEnumerator enumerator = new();
        IReadOnlyList<GpuAdapterInfo> adapters = GpuAdapterMappingService.Map(
            enumerator.EnumerateAdapters(),
            snapshot);

        foreach (GpuAdapterInfo adapter in adapters)
        {
            Console.WriteLine(adapter.Name);
            Console.WriteLine($"  LUID: 0x{unchecked((uint)adapter.Id.LuidHighPart):X8}:0x{adapter.Id.LuidLowPart:X8}");
            Console.WriteLine($"  Vendor/Device/SubSys: {adapter.Id.VendorId:X4}/{adapter.Id.DeviceId:X4}/{adapter.Id.SubSystemId:X8}");
            Console.WriteLine($"  SpecificAdapter: {adapter.SpecificAdapterKey}");
            Console.WriteLine($"  Dedicated VRAM: {FormatBytes(adapter.DedicatedVideoMemoryBytes)}");
            Console.WriteLine($"  Shared memory: {FormatBytes(adapter.SharedSystemMemoryBytes)}");
            Console.WriteLine($"  Role: {adapter.Role}");
            Console.WriteLine($"  Identity: {adapter.IdentityConfidence}");
            Console.WriteLine($"  Assignable: {adapter.IsAssignable}");
            Console.WriteLine($"  Flags: Software={adapter.IsSoftware}, Remote={adapter.IsRemote}");
            Console.WriteLine();
        }

        string? highPerformanceKey = snapshot.GlobalSettings?.HighPerformanceAdapterKey;
        if (highPerformanceKey is not null)
        {
            List<GpuAdapterInfo> matches = adapters
                .Where(adapter => SpecificAdapterKey.Matches(adapter.SpecificAdapterKey, highPerformanceKey))
                .ToList();
            string target = matches.Count == 1 ? matches[0].Name : $"未唯一匹配（{matches.Count} 个）";
            Console.WriteLine($"DirectXUserGlobalSettings.HighPerfAdapter {highPerformanceKey} -> {target}");
        }

        return 0;
    }

    private static string FormatBytes(long bytes)
    {
        const double gibibyte = 1024d * 1024d * 1024d;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{bytes / gibibyte:F2} GiB ({bytes:N0} bytes)");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("GpuProbe - GPU Preference Manager 只读诊断工具");
        Console.WriteLine("用法：GpuProbe registry | adapters | sample [--seconds 30] | inventory [--seconds 3]");
    }
}
