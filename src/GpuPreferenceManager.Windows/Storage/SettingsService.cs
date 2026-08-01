using System.Text.Json;

namespace GpuPreferenceManager.Windows.Storage;

public sealed record AppSettings(
    int SchemaVersion = 2,
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
    double? WindowTop = null);

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
        return settings.SchemaVersion < 2
            ? settings with
            {
                SchemaVersion = 2,
                SamplingIntervalSeconds = settings.SamplingIntervalSeconds == 2
                    ? 1
                    : settings.SamplingIntervalSeconds,
            }
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
