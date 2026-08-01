namespace GpuPreferenceManager.Core.Registry;

/// <summary>
/// 序列化和变换 GPU 偏好规则，同时保留非 GPU token 的原始文本及相对顺序。
/// </summary>
public static class RegistryRuleSerializer
{
    private static readonly string[] GpuFieldNames = ["SpecificAdapter", "GpuPreference"];

    /// <summary>
    /// 序列化有序 token，并统一以分号结尾。
    /// </summary>
    public static string Serialize(IEnumerable<RegistryRuleToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return string.Concat(tokens.Select(static token => token.RawText + ";"));
    }

    /// <summary>
    /// 生成指定适配器规则；模式标志按不透明常量处理。
    /// </summary>
    public static string SetSpecificAdapter(GpuPreferenceRule source, string adapterKey)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterKey);

        return ReplaceGpuFields(
            source.Tokens,
            [
                RegistryRuleToken.Create("SpecificAdapter", adapterKey),
                RegistryRuleToken.Create(
                    "GpuPreference",
                    RegistryRuleParser.SpecificAdapterModeFlag.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]);
    }

    /// <summary>
    /// 生成通用节能或高性能规则。
    /// </summary>
    public static string SetGenericPreference(GpuPreferenceRule source, GpuPreferenceKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        int value = kind switch
        {
            GpuPreferenceKind.GenericPowerSaving => 1,
            GpuPreferenceKind.GenericHighPerformance => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "仅支持通用节能或通用高性能。"),
        };

        return ReplaceGpuFields(
            source.Tokens,
            [RegistryRuleToken.Create("GpuPreference", value.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
    }

    /// <summary>
    /// 清除 GPU 字段。返回空字符串时，未来写入层应删除整个注册表值。
    /// </summary>
    public static string ClearGpuPreference(GpuPreferenceRule source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ReplaceGpuFields(source.Tokens, []);
    }

    private static string ReplaceGpuFields(
        IReadOnlyList<RegistryRuleToken> source,
        IReadOnlyList<RegistryRuleToken> replacements)
    {
        int insertionIndex = -1;
        List<RegistryRuleToken> retained = new(source.Count + replacements.Count);

        foreach (RegistryRuleToken token in source)
        {
            if (GpuFieldNames.Any(token.HasKey))
            {
                insertionIndex = insertionIndex < 0 ? retained.Count : insertionIndex;
                continue;
            }

            retained.Add(token);
        }

        insertionIndex = insertionIndex < 0 ? retained.Count : insertionIndex;
        retained.InsertRange(insertionIndex, replacements);
        return Serialize(retained);
    }
}
