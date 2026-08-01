using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace GpuPreferenceManager.Windows.Storage;

/// <summary>
/// 按可执行文件路径和最后写入时间缓存 Shell 关联图标。
/// </summary>
public sealed class ExecutableIconCache
{
    private readonly string _cacheDirectory;

    public ExecutableIconCache(ApplicationDataPaths paths)
    {
        _cacheDirectory = Path.Combine(paths.Root, "IconCache");
    }

    public async Task<string?> GetOrCreateAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            return null;
        }

        DateTime lastWriteUtc = File.GetLastWriteTimeUtc(executablePath);
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes($"{executablePath.ToUpperInvariant()}|{lastWriteUtc.Ticks}"));
        string cachePath = Path.Combine(_cacheDirectory, $"{Convert.ToHexString(identity)}.ico");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(_cacheDirectory);
        using Icon? icon = Icon.ExtractAssociatedIcon(executablePath);
        if (icon is null)
        {
            return null;
        }

        string temporaryPath = cachePath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            icon.Save(stream);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, cachePath, overwrite: true);
        return cachePath;
    }
}
