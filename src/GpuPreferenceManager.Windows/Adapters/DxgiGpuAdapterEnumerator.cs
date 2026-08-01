using GpuPreferenceManager.Core.Adapters;
using SharpGen.Runtime;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace GpuPreferenceManager.Windows.Adapters;

/// <summary>
/// 使用 DXGI 1.1 枚举当前 Windows 会话中的适配器。
/// </summary>
public sealed class DxgiGpuAdapterEnumerator : IGpuAdapterEnumerator
{
    /// <inheritdoc />
    public IReadOnlyList<GpuAdapterDescriptor> EnumerateAdapters()
    {
        List<GpuAdapterDescriptor> result = [];
        IReadOnlyDictionary<(uint LowPart, int HighPart), DisplayConfigAdapterIdentity> identities =
            DisplayConfigAdapterIdentityResolver.Resolve();
        using IDXGIFactory1 factory = CreateDXGIFactory1<IDXGIFactory1>();

        for (uint index = 0; ; index++)
        {
            Result enumResult = factory.EnumAdapters1(index, out IDXGIAdapter1? adapter);
            if (enumResult == ResultCode.NotFound)
            {
                break;
            }

            enumResult.CheckError();
            using (adapter)
            {
                AdapterDescription1 description = adapter.Description1;
                identities.TryGetValue(
                    (description.Luid.LowPart, description.Luid.HighPart),
                    out DisplayConfigAdapterIdentity? identity);
                result.Add(new GpuAdapterDescriptor(
                    new GpuAdapterId(
                        description.Luid.LowPart,
                        description.Luid.HighPart,
                        description.VendorId,
                        description.DeviceId,
                        description.SubsystemId),
                    identity?.FriendlyName ?? description.Description.TrimEnd('\0').Trim(),
                    checked((long)(ulong)description.DedicatedVideoMemory),
                    checked((long)(ulong)description.SharedSystemMemory),
                    description.Flags.HasFlag(AdapterFlags.Software),
                    description.Flags.HasFlag(AdapterFlags.Remote),
                    identity?.DeviceInstancePath,
                    identity?.IsVirtual == true));
            }
        }

        return result;
    }
}
