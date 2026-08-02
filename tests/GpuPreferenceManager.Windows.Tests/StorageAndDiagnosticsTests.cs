using System.IO.Compression;
using System.Text;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Diagnostics;
using GpuPreferenceManager.Windows.Storage;

namespace GpuPreferenceManager.Windows.Tests;

public sealed class StorageAndDiagnosticsTests
{
    [Fact]
    public async Task SettingsRoundTripAndIconCacheAreStable()
    {
        string root = CreateRoot();
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(root);
            SettingsService service = new(paths);
            AppSettings expected = new(SamplingIntervalSeconds: 5, PendingThresholdMiB: 32, Theme: "Dark");
            await service.SaveAsync(expected, CancellationToken.None);
            Assert.Equal(expected, await service.LoadAsync(CancellationToken.None));

            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("缺少当前进程路径。");
            ExecutableIconCache cache = new(paths);
            string? first = await cache.GetOrCreateAsync(executable, CancellationToken.None);
            string? second = await cache.GetOrCreateAsync(executable, CancellationToken.None);
            Assert.NotNull(first);
            Assert.Equal(first, second);
            Assert.True(File.Exists(first));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VersionOneDefaultSamplingIntervalMigratesToOneSecond()
    {
        string root = CreateRoot();
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(root);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(
                paths.SettingsPath,
                """{"SchemaVersion":1,"SamplingIntervalSeconds":2}""");

            AppSettings settings = await new SettingsService(paths).LoadAsync(CancellationToken.None);

            Assert.Equal(3, settings.SchemaVersion);
            Assert.Equal(1, settings.SamplingIntervalSeconds);
            Assert.Empty(settings.EffectiveAdapterOverrides);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdapterOverridesRoundTripInSchemaVersionThree()
    {
        string root = CreateRoot();
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(root);
            SettingsService service = new(paths);
            AppSettings expected = new(AdapterOverrides:
            [
                new(
                    @"PATH:PCI\VEN_1002&DEV_73EF\FIXTURE",
                    AdapterOverrideRole.DiscreteOrHighPerformance,
                    AdapterExclusionMode.ForceIncluded),
            ]);

            await service.SaveAsync(expected, CancellationToken.None);
            AppSettings actual = await service.LoadAsync(CancellationToken.None);

            Assert.Equal(3, actual.SchemaVersion);
            Assert.Equal(expected.EffectiveAdapterOverrides, actual.EffectiveAdapterOverrides);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RegBackupIsUtf16AndPreservesSupportedValueKinds()
    {
        string root = CreateRoot();
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(root);
            RegistrySnapshot snapshot = new(
                DateTimeOffset.UtcNow,
                true,
                [
                    new("Text", new(RegistryDataKind.Text, StringValue: "A\\B\"C"), null, false),
                    new("Number", new(RegistryDataKind.DWord, IntegerValue: 42), null, false),
                    new("Bytes", new(RegistryDataKind.Binary, BinaryValue: [0x01, 0xFE]), null, false),
                ],
                null);
            RegistryBackupService backup = new(paths, @"HKEY_CURRENT_USER\Software\Fixture");
            (string path, string hash) = await backup.ExportAsync(snapshot, "Before_Test", CancellationToken.None);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            string text = Encoding.Unicode.GetString(bytes);
            Assert.StartsWith("FFFE", Convert.ToHexString(bytes.AsSpan(0, 2)));
            Assert.Contains("Windows Registry Editor Version 5.00", text);
            Assert.Contains("\"Number\"=dword:0000002a", text);
            Assert.Contains("\"Bytes\"=hex:01,fe", text);
            Assert.Equal(64, hash.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiagnosticZipContainsRequiredFilesAndExcludesDatabase()
    {
        string root = CreateRoot();
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(root);
            paths.EnsureDirectories();
            await File.WriteAllTextAsync(paths.DatabasePath, "sensitive-history");
            DiagnosticsExportService service = new(paths, new FixtureRegistryReader(), new FixtureAdapterEnumerator());
            string path = await service.ExportAsync(CancellationToken.None);
            using ZipArchive archive = ZipFile.OpenRead(path);
            string[] names = archive.Entries.Select(static entry => entry.FullName).ToArray();
            Assert.Contains("app-version.txt", names);
            Assert.Contains("adapters.json", names);
            Assert.Contains("registry-snapshot.json", names);
            Assert.Contains("pdh-raw-sample.json", names);
            Assert.Contains("settings-redacted.json", names);
            Assert.Contains("database-schema.sql", names);
            Assert.DoesNotContain(names, static name => name.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.Tests", Guid.NewGuid().ToString("N"));

    private sealed class FixtureRegistryReader : IUserGpuPreferencesReader
    {
        public Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(
            new RegistrySnapshot(DateTimeOffset.UtcNow, true, [], null));
    }

    private sealed class FixtureAdapterEnumerator : IGpuAdapterEnumerator
    {
        public IReadOnlyList<GpuAdapterDescriptor> EnumerateAdapters() =>
            [new(new(1, 0, 0x1002, 0x73EF, 0x1EFE), "Fixture", 8L << 30, 16L << 30, false, false)];
    }
}
