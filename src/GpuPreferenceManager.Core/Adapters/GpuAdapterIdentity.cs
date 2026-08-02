namespace GpuPreferenceManager.Core.Adapters;

/// <summary>
/// 构造可跨进程启动持久化的适配器身份。LUID 仅用于本次启动，不能用于设置持久化。
/// </summary>
public static class GpuAdapterIdentity
{
    public static string? Build(GpuAdapterDescriptor descriptor, string specificAdapterKey, bool isKeyUnique)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!string.IsNullOrWhiteSpace(descriptor.DeviceInstancePath))
        {
            return $"PATH:{descriptor.DeviceInstancePath.Trim()}";
        }

        return isKeyUnique && HasValidPciIdentity(descriptor)
            ? $"KEY:{specificAdapterKey}"
            : null;
    }

    public static bool Matches(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool HasValidPciIdentity(GpuAdapterDescriptor descriptor) =>
        descriptor.Id.VendorId != 0 && descriptor.Id.DeviceId != 0;
}
