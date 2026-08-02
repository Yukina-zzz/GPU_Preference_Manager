using System.Text.Json;
using System.Text.Json.Serialization;
using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.Windows.Storage;

public sealed record AppSettings(
    int SchemaVersion = 3,
    int SamplingIntervalSeconds = 1,
    int PendingThresholdMiB = 16,
    string Theme = "System",
    int BackupCount = 100,
    bool ShowSystemProcesses = false,
    bool ShowSmallUsages = false,
    bool StartSamplingOnLaunch = true,
    double WindowWidth = 1440,
    double WindowHeight = 840,
    double? WindowLeft = null,
    double? WindowTop = null,
    GpuAdapterOverride[]? AdapterOverrides = null)
{
    [JsonIgnore]
    public IReadOnlyList<GpuAdapterOverride> EffectiveAdapterOverrides => AdapterOverrides ?? [];
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly ApplicationDataPaths _paths;

    public SettingsService(ApplicationDataPaths paths) => _paths = paths;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SettingsPath))
        {
            return new();
        }

        await using FileStream stream = File.OpenRead(_paths.SettingsPath);
        AppSettings settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken) ?? new();
        if (settings.SchemaVersion < 2)
        {
            settings = settings with
            {
                SamplingIntervalSeconds = settings.SamplingIntervalSeconds == 2
                    ? 1
                    : settings.SamplingIntervalSeconds,
            };
        }

        return settings.SchemaVersion < 3
            ? settings with { SchemaVersion = 3, AdapterOverrides = [] }
            : settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        string temporaryPath = _paths.SettingsPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        }

        File.Move(temporaryPath, _paths.SettingsPath, overwrite: true);
    }
}
