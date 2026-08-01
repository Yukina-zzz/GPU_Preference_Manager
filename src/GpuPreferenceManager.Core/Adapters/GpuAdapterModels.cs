namespace GpuPreferenceManager.Core.Adapters;

/// <summary>
/// 由 LUID 和 PCI 身份共同组成的适配器标识。
/// </summary>
public readonly record struct GpuAdapterId(
    uint LuidLowPart,
    int LuidHighPart,
    uint VendorId,
    uint DeviceId,
    uint SubSystemId);

/// <summary>
/// 适配器角色。
/// </summary>
public enum GpuAdapterRole
{
    Unknown,
    IntegratedOrPowerSaving,
    DiscreteOrHighPerformance,
    Other,
    Excluded,
}

/// <summary>
/// SpecificAdapter 身份的可信度。
/// </summary>
public enum AdapterIdentityConfidence
{
    VerifiedByExistingRule,
    DerivedFromDxgi,
    UserConfirmed,
    Ambiguous,
}

/// <summary>
/// DXGI 层返回、尚未结合注册表判断的适配器描述。
/// </summary>
public sealed record GpuAdapterDescriptor(
    GpuAdapterId Id,
    string Name,
    long DedicatedVideoMemoryBytes,
    long SharedSystemMemoryBytes,
    bool IsSoftware,
    bool IsRemote,
    string? DeviceInstancePath = null,
    bool IsVirtual = false);

/// <summary>
/// 可供业务层使用的适配器信息。
/// </summary>
public sealed record GpuAdapterInfo(
    GpuAdapterId Id,
    string Name,
    long DedicatedVideoMemoryBytes,
    long SharedSystemMemoryBytes,
    string SpecificAdapterKey,
    GpuAdapterRole Role,
    AdapterIdentityConfidence IdentityConfidence,
    bool IsSoftware,
    bool IsRemote,
    bool IsAssignable,
    string? DeviceInstancePath = null,
    bool IsVirtual = false);

/// <summary>
/// DXGI 适配器枚举契约。
/// </summary>
public interface IGpuAdapterEnumerator
{
    IReadOnlyList<GpuAdapterDescriptor> EnumerateAdapters();
}
