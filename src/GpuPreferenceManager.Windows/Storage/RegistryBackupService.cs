using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Windows.Storage;

public sealed class RegistryBackupService
{
    private const string RegistryHeader = "Windows Registry Editor Version 5.00";
    private readonly ApplicationDataPaths _paths;
    private readonly string _registryPath;

    public RegistryBackupService(
        ApplicationDataPaths paths,
        string registryPath = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences")
    {
        _paths = paths;
        _registryPath = registryPath;
    }

    public async Task<(string Path, string Sha256)> ExportAsync(
        RegistrySnapshot snapshot,
        string label,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        string safeLabel = string.Concat(label.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string fileName = $"{safeLabel}_{DateTimeOffset.Now:yyyyMMdd_HHmmssfff}.reg";
        string path = Path.Combine(_paths.BackupDirectory, fileName);
        StringBuilder builder = new();
        builder.AppendLine(RegistryHeader).AppendLine().Append('[').Append(_registryPath).AppendLine("]");
        foreach (RegistryValueSnapshot value in snapshot.Values.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(FormatName(value.Name)).Append('=').AppendLine(FormatData(value.Data));
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.Unicode, cancellationToken);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return (path, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public void Cleanup(int keepCount = 100)
    {
        if (!Directory.Exists(_paths.BackupDirectory))
        {
            return;
        }

        foreach (FileInfo file in new DirectoryInfo(_paths.BackupDirectory)
                     .EnumerateFiles("Before_*.reg")
                     .OrderByDescending(static file => file.CreationTimeUtc)
                     .Skip(keepCount))
        {
            file.Delete();
        }
    }

    private static string FormatName(string name) => name.Length == 0 ? "@" : $"\"{Escape(name)}\"";

    private static string FormatData(RegistryValueData data) => data.Kind switch
    {
        RegistryDataKind.Text when data.StringValue is not null && !ContainsLineBreak(data.StringValue) =>
            $"\"{Escape(data.StringValue)}\"",
        RegistryDataKind.Text => FormatHex(1, Encoding.Unicode.GetBytes((data.StringValue ?? string.Empty) + '\0')),
        RegistryDataKind.ExpandableText => FormatHex(2, Encoding.Unicode.GetBytes((data.StringValue ?? string.Empty) + '\0')),
        RegistryDataKind.Binary => FormatHex(null, data.BinaryValue ?? []),
        RegistryDataKind.None => FormatHex(0, data.BinaryValue ?? []),
        RegistryDataKind.DWord => $"dword:{unchecked((uint)(data.IntegerValue ?? 0)):x8}",
        RegistryDataKind.QWord => FormatHex(11, BitConverter.GetBytes(data.IntegerValue ?? 0)),
        RegistryDataKind.StringList => FormatHex(
            7,
            Encoding.Unicode.GetBytes(string.Join('\0', data.MultiStringValue ?? []) + "\0\0")),
        _ => FormatHex(0, []),
    };

    private static bool ContainsLineBreak(string value) => value.Contains('\r') || value.Contains('\n');

    private static string FormatHex(int? kind, byte[] data)
    {
        string prefix = kind is null ? "hex:" : $"hex({kind.Value.ToString("x", CultureInfo.InvariantCulture)}):";
        return prefix + string.Join(',', data.Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
