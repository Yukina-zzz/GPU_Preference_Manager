namespace GpuPreferenceManager.Core.Adapters;

/// <summary>
/// 生成和匹配 Windows SpecificAdapter 键。
/// </summary>
public static class SpecificAdapterKey
{
    /// <summary>
    /// 按 VendorId&amp;DeviceId&amp;SubSysId 生成大写十六进制键；SubSysId 去除前导零。
    /// </summary>
    public static string Build(uint vendorId, uint deviceId, uint subSystemId) =>
        $"{vendorId:X4}&{deviceId:X4}&{subSystemId:X}";

    /// <summary>
    /// 与注册表键进行不区分大小写的完整匹配。
    /// </summary>
    public static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
