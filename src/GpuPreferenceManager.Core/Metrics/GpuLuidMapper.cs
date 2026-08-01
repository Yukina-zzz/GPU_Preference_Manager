using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.Core.Metrics;

public sealed class GpuLuidMapper
{
    private LuidOrder _knownOrder;

    public bool TryMap(
        PdhGpuInstance instance,
        IReadOnlyList<GpuAdapterDescriptor> adapters,
        out GpuAdapterId adapterId)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(adapters);

        if (_knownOrder != LuidOrder.Unknown
            && TryFind(instance, adapters, _knownOrder, out adapterId))
        {
            return true;
        }

        bool conventional = TryFind(instance, adapters, LuidOrder.HighThenLow, out GpuAdapterId conventionalId);
        bool swapped = TryFind(instance, adapters, LuidOrder.LowThenHigh, out GpuAdapterId swappedId);
        if (conventional == swapped)
        {
            adapterId = default;
            return false;
        }

        _knownOrder = conventional ? LuidOrder.HighThenLow : LuidOrder.LowThenHigh;
        adapterId = conventional ? conventionalId : swappedId;
        return true;
    }

    private static bool TryFind(
        PdhGpuInstance instance,
        IReadOnlyList<GpuAdapterDescriptor> adapters,
        LuidOrder order,
        out GpuAdapterId adapterId)
    {
        uint low = order == LuidOrder.HighThenLow ? instance.SecondLuidPart : instance.FirstLuidPart;
        int high = unchecked((int)(order == LuidOrder.HighThenLow
            ? instance.FirstLuidPart
            : instance.SecondLuidPart));
        List<GpuAdapterDescriptor> matches = adapters
            .Where(adapter => adapter.Id.LuidLowPart == low && adapter.Id.LuidHighPart == high)
            .ToList();
        if (matches.Count == 1)
        {
            adapterId = matches[0].Id;
            return true;
        }

        adapterId = default;
        return false;
    }

    private enum LuidOrder
    {
        Unknown,
        HighThenLow,
        LowThenHigh,
    }
}
