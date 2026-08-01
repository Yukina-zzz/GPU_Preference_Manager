using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Windows.Adapters;

namespace GpuPreferenceManager.IntegrationTests;

public sealed class DxgiGpuAdapterEnumeratorTests
{
    [WindowsHardwareFact]
    public void EnumeratesAtLeastOneAdapterWithAStableIdentity()
    {
        DxgiGpuAdapterEnumerator enumerator = new();

        IReadOnlyList<GpuAdapterDescriptor> adapters = enumerator.EnumerateAdapters();

        Assert.NotEmpty(adapters);
        Assert.Contains(adapters, adapter => adapter.Id.VendorId != 0 && adapter.Id.DeviceId != 0);
        Assert.All(
            adapters.Where(adapter => adapter.Id.VendorId != 0 && adapter.Id.DeviceId != 0),
            adapter => Assert.NotEmpty(SpecificAdapterKey.Build(
                adapter.Id.VendorId,
                adapter.Id.DeviceId,
                adapter.Id.SubSystemId)));
        Assert.All(
            adapters.Where(static adapter => adapter.IsVirtual),
            adapter =>
            {
                Assert.StartsWith(@"ROOT\DISPLAY\", adapter.DeviceInstancePath, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
            });
    }
}
