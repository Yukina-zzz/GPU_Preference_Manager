using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace GpuPreferenceManager.Windows.Adapters;

internal static class DisplayConfigAdapterIdentityResolver
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyDictionary<(uint LowPart, int HighPart), DisplayConfigAdapterIdentity> Resolve()
    {
        const QUERY_DISPLAY_CONFIG_FLAGS flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ALL_PATHS;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            WIN32_ERROR sizeStatus = PInvoke.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if ((uint)sizeStatus != ErrorSuccess)
            {
                return new Dictionary<(uint, int), DisplayConfigAdapterIdentity>();
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            WIN32_ERROR queryStatus = PInvoke.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes);
            if ((uint)queryStatus == ErrorInsufficientBuffer)
            {
                continue;
            }

            if ((uint)queryStatus != ErrorSuccess)
            {
                return new Dictionary<(uint, int), DisplayConfigAdapterIdentity>();
            }

            return paths.Take(checked((int)pathCount))
                .SelectMany(static path => new[] { path.sourceInfo.adapterId, path.targetInfo.adapterId })
                .DistinctBy(static luid => (luid.LowPart, luid.HighPart))
                .Select(TryResolve)
                .Where(static identity => identity is not null)
                .ToDictionary(
                    static identity => (identity!.LuidLowPart, identity.LuidHighPart),
                    static identity => identity!);
        }

        return new Dictionary<(uint, int), DisplayConfigAdapterIdentity>();
    }

    private static DisplayConfigAdapterIdentity? TryResolve(LUID luid)
    {
        DISPLAYCONFIG_ADAPTER_NAME request = new();
        request.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME;
        request.header.size = checked((uint)Marshal.SizeOf<DISPLAYCONFIG_ADAPTER_NAME>());
        request.header.adapterId = luid;
        if (PInvoke.DisplayConfigGetDeviceInfo(ref request.header) != ErrorSuccess)
        {
            return null;
        }

        string deviceInterfacePath = request.adapterDevicePath.ToString();
        string? deviceInstancePath = ToDeviceInstancePath(deviceInterfacePath);
        string? friendlyName = deviceInstancePath is null ? null : ReadFriendlyName(deviceInstancePath);
        bool isVirtual = deviceInstancePath?.StartsWith(@"ROOT\DISPLAY\", StringComparison.OrdinalIgnoreCase) == true;
        return new(
            luid.LowPart,
            luid.HighPart,
            deviceInterfacePath,
            deviceInstancePath,
            friendlyName,
            isVirtual);
    }

    internal static string? ToDeviceInstancePath(string deviceInterfacePath)
    {
        if (!deviceInterfacePath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = deviceInterfacePath[4..].Split('#');
        return parts.Length >= 4
            ? string.Join("\\", parts.Take(parts.Length - 1))
            : null;
    }

    internal static string? NormalizeDeviceDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        int separator = description.LastIndexOf(';');
        return separator >= 0 && separator + 1 < description.Length
            ? description[(separator + 1)..]
            : description;
    }

    private static string? ReadFriendlyName(string deviceInstancePath)
    {
        using RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Enum\{deviceInstancePath}",
            writable: false);
        return NormalizeDeviceDescription(
            key?.GetValue("FriendlyName") as string
            ?? key?.GetValue("DeviceDesc") as string);
    }
}

internal sealed record DisplayConfigAdapterIdentity(
    uint LuidLowPart,
    int LuidHighPart,
    string DeviceInterfacePath,
    string? DeviceInstancePath,
    string? FriendlyName,
    bool IsVirtual);
