using System.IO.Compression;
using System.Text.Json;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Metrics;
using GpuPreferenceManager.Windows.Storage;

namespace GpuPreferenceManager.Windows.Diagnostics;

public sealed class DiagnosticsExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ApplicationDataPaths _paths;
    private readonly IUserGpuPreferencesReader _registry;
    private readonly IGpuAdapterEnumerator _adapters;

    public DiagnosticsExportService(
        ApplicationDataPaths paths,
        IUserGpuPreferencesReader registry,
        IGpuAdapterEnumerator adapters)
    {
        _paths = paths;
        _registry = registry;
        _adapters = adapters;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        string path = Path.Combine(
            _paths.DiagnosticsDirectory,
            $"Diagnostics_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.zip");
        RegistrySnapshot registry = await _registry.ReadSnapshotAsync(cancellationToken);
        IReadOnlyList<GpuAdapterDescriptor> adapters = _adapters.EnumerateAdapters();
        await using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: false);
        await WriteTextAsync(
            archive,
            "app-version.txt",
            $"Version: {typeof(DiagnosticsExportService).Assembly.GetName().Version}\nOS: {Environment.OSVersion}\nUTC: {DateTimeOffset.UtcNow:O}\n",
            cancellationToken);
        await WriteTextAsync(archive, "adapters.json", JsonSerializer.Serialize(adapters, JsonOptions), cancellationToken);
        await WriteTextAsync(archive, "registry-snapshot.json", JsonSerializer.Serialize(registry, JsonOptions), cancellationToken);
        await WriteTextAsync(archive, "pdh-raw-sample.json", await CapturePdhSampleAsync(cancellationToken), cancellationToken);
        await WriteTextAsync(
            archive,
            "settings-redacted.json",
            File.Exists(_paths.SettingsPath) ? await File.ReadAllTextAsync(_paths.SettingsPath, cancellationToken) : "{}",
            cancellationToken);
        await WriteTextAsync(archive, "database-schema.sql", SqliteHistoryStore.DatabaseSchema, cancellationToken);
        if (Directory.Exists(_paths.LogDirectory))
        {
            foreach (string log in Directory.EnumerateFiles(_paths.LogDirectory, "*.log"))
            {
                ZipArchiveEntry entry = archive.CreateEntry($"logs/{Path.GetFileName(log)}", CompressionLevel.SmallestSize);
                await using Stream target = entry.Open();
                await using FileStream source = File.OpenRead(log);
                await source.CopyToAsync(target, cancellationToken);
            }
        }

        return path;
    }

    private static async Task<string> CapturePdhSampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using PdhGpuMetricsSampler sampler = new();
            await foreach (var sample in sampler.SampleAsync(TimeSpan.FromMilliseconds(250), cancellationToken))
            {
                return JsonSerializer.Serialize(sample, JsonOptions);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { Error = exception.Message }, JsonOptions);
        }

        return "{}";
    }

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        await using Stream stream = entry.Open();
        await using StreamWriter writer = new(stream);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }
}
