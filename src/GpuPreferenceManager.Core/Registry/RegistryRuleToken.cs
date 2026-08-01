namespace GpuPreferenceManager.Core.Registry;

/// <summary>
/// 表示注册表规则字符串中的一个有序 token。
/// </summary>
public sealed record RegistryRuleToken(
    string RawText,
    string? Key,
    string? Value,
    bool HasEquals)
{
    /// <summary>
    /// 判断 token 是否具有指定键名，比较不区分大小写。
    /// </summary>
    public bool HasKey(string key) =>
        HasEquals && string.Equals(Key, key, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 创建规范化的键值 token。
    /// </summary>
    public static RegistryRuleToken Create(string key, string value) =>
        new($"{key}={value}", key, value, true);
}
