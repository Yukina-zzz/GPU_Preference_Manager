using System.Globalization;

namespace GpuPreferenceManager.Core.Registry;

/// <summary>
/// 解析 UserGpuPreferences 的 REG_SZ 内容，并保留未知或异常 token。
/// </summary>
public static class RegistryRuleParser
{
    /// <summary>
    /// Windows 当前用于 SpecificAdapter 模式的不透明标志。
    /// </summary>
    public const int SpecificAdapterModeFlag = 0x40000000;

    /// <summary>
    /// 解析规则。<paramref name="rawValue"/> 为 <see langword="null"/> 表示注册表值不存在。
    /// </summary>
    public static GpuPreferenceRule Parse(string? rawValue)
    {
        if (rawValue is null)
        {
            return new(GpuPreferenceKind.NoRule, null, null, [], string.Empty);
        }

        IReadOnlyList<RegistryRuleToken> tokens = Tokenize(rawValue);
        RegistryRuleToken? specificToken = tokens.LastOrDefault(
            static token => token.HasKey("SpecificAdapter"));
        RegistryRuleToken? preferenceToken = tokens.LastOrDefault(
            static token => token.HasKey("GpuPreference"));

        string? specificAdapter = string.IsNullOrWhiteSpace(specificToken?.Value)
            ? null
            : specificToken.Value.Trim();

        int? preference = int.TryParse(
            preferenceToken?.Value?.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsedPreference)
            ? parsedPreference
            : null;

        GpuPreferenceKind kind = Classify(tokens, specificToken, specificAdapter, preferenceToken, preference);
        return new(kind, specificAdapter, preference, tokens, rawValue);
    }

    /// <summary>
    /// 把规则字符串拆分为有序 token；只省略由最后一个分号产生的终止空项。
    /// </summary>
    public static IReadOnlyList<RegistryRuleToken> Tokenize(string rawValue)
    {
        ArgumentNullException.ThrowIfNull(rawValue);

        string[] parts = rawValue.Split(';');
        int count = parts.Length;
        if (count > 0 && parts[^1].Length == 0)
        {
            count--;
        }

        List<RegistryRuleToken> tokens = new(count);
        for (int index = 0; index < count; index++)
        {
            string rawToken = parts[index];
            int equalsIndex = rawToken.IndexOf('=');
            if (equalsIndex < 0)
            {
                tokens.Add(new(rawToken, null, null, false));
                continue;
            }

            string key = rawToken[..equalsIndex].Trim();
            string value = rawToken[(equalsIndex + 1)..];
            tokens.Add(new(rawToken, key, value, true));
        }

        return tokens;
    }

    private static GpuPreferenceKind Classify(
        IReadOnlyList<RegistryRuleToken> tokens,
        RegistryRuleToken? specificToken,
        string? specificAdapter,
        RegistryRuleToken? preferenceToken,
        int? preference)
    {
        bool hasMalformedToken = tokens.Any(static token => !token.HasEquals);
        bool hasSpecificField = specificToken is not null;
        bool hasPreferenceField = preferenceToken is not null;

        if (hasSpecificField || hasPreferenceField)
        {
            if (specificAdapter is not null && preference == SpecificAdapterModeFlag)
            {
                return GpuPreferenceKind.SpecificAdapter;
            }

            if (!hasSpecificField && preference == 1)
            {
                return GpuPreferenceKind.GenericPowerSaving;
            }

            if (!hasSpecificField && preference == 2)
            {
                return GpuPreferenceKind.GenericHighPerformance;
            }

            if (!hasSpecificField && preference == 0)
            {
                return GpuPreferenceKind.WindowsDecides;
            }

            return GpuPreferenceKind.Unknown;
        }

        return hasMalformedToken ? GpuPreferenceKind.Unknown : GpuPreferenceKind.WindowsDecides;
    }
}
