namespace GpuPreferenceManager.Core.Registry;

/// <summary>
/// GPU 偏好规则分类。
/// </summary>
public enum GpuPreferenceKind
{
    NoRule,
    WindowsDecides,
    GenericPowerSaving,
    GenericHighPerformance,
    SpecificAdapter,
    Unknown,
}

/// <summary>
/// 已解析但保留原始信息的 GPU 偏好规则。
/// </summary>
public sealed record GpuPreferenceRule(
    GpuPreferenceKind Kind,
    string? SpecificAdapterKey,
    int? RawGpuPreference,
    IReadOnlyList<RegistryRuleToken> Tokens,
    string RawValue);
