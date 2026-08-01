namespace GpuPreferenceManager.Core.Registry;

/// <summary>
/// 与 Microsoft.Win32 解耦的注册表值类型。
/// </summary>
public enum RegistryDataKind
{
    None,
    Text,
    ExpandableText,
    Binary,
    DWord,
    StringList,
    QWord,
    Unknown,
}

/// <summary>
/// 注册表值的只读数据副本。
/// </summary>
public sealed record RegistryValueData(
    RegistryDataKind Kind,
    string? StringValue = null,
    byte[]? BinaryValue = null,
    IReadOnlyList<string>? MultiStringValue = null,
    long? IntegerValue = null);

/// <summary>
/// 一个 UserGpuPreferences 注册表值。
/// </summary>
public sealed record RegistryValueSnapshot(
    string Name,
    RegistryValueData Data,
    GpuPreferenceRule? Rule,
    bool IsGlobalSettings);

/// <summary>
/// DirectXUserGlobalSettings 的只读解析结果。
/// </summary>
public sealed record DirectXGlobalSettings(
    string RawValue,
    string? HighPerformanceAdapterKey,
    IReadOnlyList<RegistryRuleToken> Tokens);

/// <summary>
/// UserGpuPreferences 键在同一时刻的完整只读快照。
/// </summary>
public sealed record RegistrySnapshot(
    DateTimeOffset CapturedAtUtc,
    bool KeyExists,
    IReadOnlyList<RegistryValueSnapshot> Values,
    DirectXGlobalSettings? GlobalSettings)
{
    /// <summary>
    /// 应用规则，不包含 DirectXUserGlobalSettings。
    /// </summary>
    public IEnumerable<RegistryValueSnapshot> ApplicationValues =>
        Values.Where(static value => !value.IsGlobalSettings);
}

/// <summary>
/// UserGpuPreferences 只读仓储契约；写入能力由 Windows 层的独立接口提供。
/// </summary>
public interface IUserGpuPreferencesReader
{
    Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}
