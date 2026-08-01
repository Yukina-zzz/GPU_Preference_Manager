using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GpuPreferenceManager.Core.Metrics;
using Windows.Win32;
using Windows.Win32.System.Performance;

namespace GpuPreferenceManager.Windows.Metrics;

public sealed class PdhGpuMetricsSampler : IGpuMetricsSampler
{
    private const string DedicatedPath = @"\GPU Process Memory(*)\Dedicated Usage";
    private const string SharedPath = @"\GPU Process Memory(*)\Shared Usage";
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";

    private readonly PdhCloseQuerySafeHandle _query;
    private readonly PDH_HCOUNTER _dedicatedCounter;
    private readonly PDH_HCOUNTER _sharedCounter;
    private readonly PDH_HCOUNTER _engineCounter;
    private bool _disposed;

    public PdhGpuMetricsSampler()
    {
        ThrowIfPdhError(PInvoke.PdhOpenQuery(null!, 0, out _query), "PdhOpenQuery");
        try
        {
            ThrowIfPdhError(PInvoke.PdhAddEnglishCounter(_query, DedicatedPath, 0, out _dedicatedCounter), DedicatedPath);
            ThrowIfPdhError(PInvoke.PdhAddEnglishCounter(_query, SharedPath, 0, out _sharedCounter), SharedPath);
            ThrowIfPdhError(PInvoke.PdhAddEnglishCounter(_query, EnginePath, 0, out _engineCounter), EnginePath);
        }
        catch
        {
            _query.Dispose();
            throw;
        }
    }

    public async IAsyncEnumerable<GpuMetricsSnapshot> SampleAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interval < TimeSpan.FromMilliseconds(250))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "采样间隔不能小于 250 毫秒。");
        }

        bool firstSample = true;
        using PeriodicTimer timer = new(interval);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint collectStatus = PInvoke.PdhCollectQueryData((PDH_HQUERY)_query.DangerousGetHandle());
            if (collectStatus != 0 && collectStatus != PInvoke.PDH_NO_DATA)
            {
                ThrowIfPdhError(collectStatus, "PdhCollectQueryData");
            }

            List<string> unparsed = [];
            List<RawGpuCounterValue> values = [];
            values.AddRange(ReadCounter(_dedicatedCounter, GpuCounterKind.DedicatedMemory, useDouble: false, unparsed));
            values.AddRange(ReadCounter(_sharedCounter, GpuCounterKind.SharedMemory, useDouble: false, unparsed));
            if (!firstSample)
            {
                values.AddRange(ReadCounter(_engineCounter, GpuCounterKind.EngineUtilization, useDouble: true, unparsed));
            }

            firstSample = false;
            yield return new(DateTimeOffset.UtcNow, values, unparsed);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _query.Dispose();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    private static unsafe List<RawGpuCounterValue> ReadCounter(
        PDH_HCOUNTER counter,
        GpuCounterKind kind,
        bool useDouble,
        List<string> unparsed)
    {
        uint bufferSize = 0;
        uint itemCount = 0;
        PDH_FMT format = useDouble ? PDH_FMT.PDH_FMT_DOUBLE : PDH_FMT.PDH_FMT_LARGE;
        uint status = PInvoke.PdhGetFormattedCounterArray(counter, format, &bufferSize, &itemCount, null);
        if (status == PInvoke.PDH_NO_DATA || bufferSize == 0)
        {
            return [];
        }

        if (status != PInvoke.PDH_MORE_DATA)
        {
            ThrowIfPdhError(status, "PdhGetFormattedCounterArray(size)");
        }

        const int MaxResizeAttempts = 4;
        for (int attempt = 0; attempt < MaxResizeAttempts; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
            try
            {
                var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)buffer;
                status = PInvoke.PdhGetFormattedCounterArray(counter, format, &bufferSize, &itemCount, items);
                if (status == PInvoke.PDH_NO_DATA)
                {
                    return [];
                }

                if (status == PInvoke.PDH_MORE_DATA)
                {
                    continue;
                }

                ThrowIfPdhError(status, "PdhGetFormattedCounterArray(data)");

                List<RawGpuCounterValue> result = new(checked((int)itemCount));
                for (uint index = 0; index < itemCount; index++)
                {
                    PDH_FMT_COUNTERVALUE_ITEM_W item = items[index];
                    if (item.FmtValue.CStatus != PInvoke.PDH_CSTATUS_VALID_DATA
                        && item.FmtValue.CStatus != PInvoke.PDH_CSTATUS_NEW_DATA)
                    {
                        continue;
                    }

                    string instanceName = item.szName.ToString();
                    if (!PdhGpuInstanceNameParser.TryParse(instanceName, out PdhGpuInstance? instance))
                    {
                        unparsed.Add(instanceName);
                        continue;
                    }

                    double value = useDouble ? item.FmtValue.doubleValue : item.FmtValue.largeValue;
                    result.Add(new(kind, instance!, Math.Max(0, value), item.FmtValue.CStatus));
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("PdhGetFormattedCounterArray(data) 的实例列表持续变化，无法取得稳定快照。");
    }

    private static void ThrowIfPdhError(uint status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{operation} 失败，PDH 状态 0x{status:X8}。");
        }
    }
}
