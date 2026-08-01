using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Core.Registry;
using Microsoft.Win32;

namespace GpuPreferenceManager.Windows.Registry;

public sealed class WindowsGpuPreferenceRegistry : IGpuPreferenceRegistry
{
    private readonly RegistryHive _hive;
    private readonly RegistryView _view;
    private readonly string _subKeyPath;
    private readonly WindowsUserGpuPreferencesReader _reader;

    public WindowsGpuPreferenceRegistry(
        string subKeyPath = WindowsUserGpuPreferencesReader.DefaultSubKeyPath,
        RegistryHive hive = RegistryHive.CurrentUser,
        RegistryView view = RegistryView.Default)
    {
        _subKeyPath = subKeyPath;
        _hive = hive;
        _view = view;
        _reader = new(subKeyPath, hive, view);
    }

    public Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) =>
        _reader.ReadSnapshotAsync(cancellationToken);

    public Task<RegistryValueState> ReadValueAsync(string valueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(_hive, _view);
        using RegistryKey? key = baseKey.OpenSubKey(_subKeyPath, writable: false);
        if (key is null || !key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(new RegistryValueState(false, null, null));
        }

        RegistryValueKind kind;
        object? value;
        try
        {
            kind = key.GetValueKind(valueName);
            value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (IOException)
        {
            return Task.FromResult(new RegistryValueState(false, null, null));
        }

        RegistryDataKind dataKind = kind switch
        {
            RegistryValueKind.String => RegistryDataKind.Text,
            RegistryValueKind.ExpandString => RegistryDataKind.ExpandableText,
            RegistryValueKind.Binary => RegistryDataKind.Binary,
            RegistryValueKind.DWord => RegistryDataKind.DWord,
            RegistryValueKind.MultiString => RegistryDataKind.StringList,
            RegistryValueKind.QWord => RegistryDataKind.QWord,
            RegistryValueKind.None => RegistryDataKind.None,
            _ => RegistryDataKind.Unknown,
        };
        return Task.FromResult(new RegistryValueState(true, dataKind, value as string));
    }

    public Task WriteValueAsync(string valueName, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(_hive, _view);
        using RegistryKey key = baseKey.CreateSubKey(_subKeyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
        key.Flush();
        return Task.CompletedTask;
    }

    public Task DeleteValueAsync(string valueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        cancellationToken.ThrowIfCancellationRequested();
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(_hive, _view);
        using RegistryKey? key = baseKey.OpenSubKey(_subKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
        key?.Flush();
        return Task.CompletedTask;
    }
}
