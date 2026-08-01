using GpuPreferenceManager.Core.Registry;
using Microsoft.Win32;

namespace GpuPreferenceManager.Windows.Registry;

/// <summary>
/// 以只读方式获取 Windows DirectX UserGpuPreferences 快照。
/// </summary>
public sealed class WindowsUserGpuPreferencesReader : IUserGpuPreferencesReader
{
    public const string DefaultSubKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string GlobalSettingsValueName = "DirectXUserGlobalSettings";

    private readonly RegistryHive _hive;
    private readonly RegistryView _view;
    private readonly string _subKeyPath;

    /// <summary>
    /// 创建只读仓储。可覆盖 hive 和路径，以便集成测试使用临时测试键。
    /// </summary>
    public WindowsUserGpuPreferencesReader(
        string subKeyPath = DefaultSubKeyPath,
        RegistryHive hive = RegistryHive.CurrentUser,
        RegistryView view = RegistryView.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        _subKeyPath = subKeyPath;
        _hive = hive;
        _view = view;
    }

    /// <inheritdoc />
    public Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using RegistryKey baseKey = RegistryKey.OpenBaseKey(_hive, _view);
        using RegistryKey? key = baseKey.OpenSubKey(_subKeyPath, writable: false);
        if (key is null)
        {
            return Task.FromResult(new RegistrySnapshot(
                DateTimeOffset.UtcNow,
                false,
                [],
                null));
        }

        List<RegistryValueSnapshot> values = [];
        DirectXGlobalSettings? globalSettings = null;

        foreach (string valueName in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegistryValueKind nativeKind;
            object? nativeValue;
            try
            {
                nativeKind = key.GetValueKind(valueName);
                nativeValue = key.GetValue(
                    valueName,
                    defaultValue: null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch (IOException)
            {
                // 注册表值可能在枚举后被 Windows 设置或其他进程删除；跳过已消失项。
                continue;
            }

            RegistryValueData data = ConvertValue(nativeKind, nativeValue);
            bool isGlobal = string.Equals(
                valueName,
                GlobalSettingsValueName,
                StringComparison.OrdinalIgnoreCase);

            GpuPreferenceRule? rule = !isGlobal && data.StringValue is not null
                ? RegistryRuleParser.Parse(data.StringValue)
                : null;

            if (isGlobal && data.StringValue is not null)
            {
                IReadOnlyList<RegistryRuleToken> tokens = RegistryRuleParser.Tokenize(data.StringValue);
                string? highPerformanceKey = tokens.LastOrDefault(
                    static token => token.HasKey("HighPerfAdapter"))?.Value?.Trim();
                globalSettings = new(data.StringValue, highPerformanceKey, tokens);
            }

            values.Add(new(valueName, data, rule, isGlobal));
        }

        return Task.FromResult(new RegistrySnapshot(
            DateTimeOffset.UtcNow,
            true,
            values,
            globalSettings));
    }

    private static RegistryValueData ConvertValue(RegistryValueKind kind, object? value) => kind switch
    {
        RegistryValueKind.String => new(RegistryDataKind.Text, StringValue: value as string),
        RegistryValueKind.ExpandString => new(RegistryDataKind.ExpandableText, StringValue: value as string),
        RegistryValueKind.Binary => new(RegistryDataKind.Binary, BinaryValue: value as byte[]),
        RegistryValueKind.None => new(RegistryDataKind.None, BinaryValue: value as byte[]),
        RegistryValueKind.DWord => new(RegistryDataKind.DWord, IntegerValue: value is int dword ? dword : null),
        RegistryValueKind.QWord => new(RegistryDataKind.QWord, IntegerValue: value is long qword ? qword : null),
        RegistryValueKind.MultiString => new(
            RegistryDataKind.StringList,
            MultiStringValue: value is string[] strings ? strings : null),
        _ => new(RegistryDataKind.Unknown, StringValue: value?.ToString()),
    };
}
