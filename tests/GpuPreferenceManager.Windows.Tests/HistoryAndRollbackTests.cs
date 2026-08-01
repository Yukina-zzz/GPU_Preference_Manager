using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.History;
using GpuPreferenceManager.Windows.Registry;
using GpuPreferenceManager.Windows.Storage;
using Microsoft.Win32;

namespace GpuPreferenceManager.Windows.Tests;

public sealed class HistoryAndRollbackTests
{
    [Fact]
    public async Task RejectsNonStringRegistryValueWithoutChangingItsTypeOrData()
    {
        string registryPath = $@"Software\GpuPreferenceManager.Tests\{Guid.NewGuid():N}";
        string dataRoot = Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.Tests", Guid.NewGuid().ToString("N"));
        const string executable = @"C:\Fixture\BinaryValue.exe";
        byte[] original = [0x10, 0x20, 0x30, 0x40];
        try
        {
            using (RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath, writable: true))
            {
                key.SetValue(executable, original, RegistryValueKind.Binary);
            }

            ApplicationDataPaths paths = ApplicationDataPaths.Create(dataRoot);
            WindowsGpuPreferenceRegistry registry = new(registryPath);
            SqliteHistoryStore history = new(paths);
            RegistryBackupService backup = new(paths, $@"HKEY_CURRENT_USER\{registryPath}");
            using GpuPreferenceChangeService changes = new(registry, history, backup, () => [CreateAdapter()]);

            ChangeResult result = await changes.ApplyPreferenceAsync(
                [executable],
                GpuPreferenceTarget.GenericHighPerformance,
                null,
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("仅支持无损修改 REG_SZ", result.Message);
            using RegistryKey verificationKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath, writable: false)!;
            Assert.Equal(RegistryValueKind.Binary, verificationKey.GetValueKind(executable));
            Assert.Equal(original, Assert.IsType<byte[]>(verificationKey.GetValue(executable)));
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AppliesVerifiesBacksUpAndUndoesRuleInIsolatedRegistryKey()
    {
        string registryPath = $@"Software\GpuPreferenceManager.Tests\{Guid.NewGuid():N}";
        string dataRoot = Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.Tests", Guid.NewGuid().ToString("N"));
        const string executable = @"C:\Fixture\Game.exe";
        const string original = "Future=keep;GpuPreference=1;";
        try
        {
            using (RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath, writable: true))
            {
                key.SetValue(executable, original, RegistryValueKind.String);
            }

            ApplicationDataPaths paths = ApplicationDataPaths.Create(dataRoot);
            WindowsGpuPreferenceRegistry registry = new(registryPath);
            SqliteHistoryStore history = new(paths);
            RegistryBackupService backup = new(paths, $@"HKEY_CURRENT_USER\{registryPath}");
            GpuAdapterInfo adapter = CreateAdapter();
            using GpuPreferenceChangeService changes = new(registry, history, backup, () => [adapter]);

            ChangeResult applied = await changes.ApplyPreferenceAsync(
                [executable],
                GpuPreferenceTarget.GenericHighPerformance,
                null,
                CancellationToken.None);

            Assert.True(applied.Succeeded);
            Assert.Equal("Future=keep;GpuPreference=2;", (await registry.ReadValueAsync(executable, CancellationToken.None)).StringValue);
            Assert.Single(Directory.EnumerateFiles(paths.BackupDirectory, "*.reg"));
            using RollbackService rollback = new(registry, history, backup);
            ChangeResult undone = await rollback.UndoAsync(applied.TransactionId!.Value, ConflictPolicy.Stop, CancellationToken.None);
            Assert.True(undone.Succeeded);
            Assert.Equal(original, (await registry.ReadValueAsync(executable, CancellationToken.None)).StringValue);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BaselineIsCreatedOnlyOnce()
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(dataRoot);
            SqliteHistoryStore store = new(paths);
            var snapshot = new GpuPreferenceManager.Core.Registry.RegistrySnapshot(
                DateTimeOffset.UtcNow,
                true,
                [],
                null);
            await store.EnsureBaselineAsync(snapshot, [], CancellationToken.None);
            string? first = await store.GetBaselineRegistryJsonAsync(CancellationToken.None);
            await store.EnsureBaselineAsync(snapshot with { KeyExists = false }, [], CancellationToken.None);
            string? second = await store.GetBaselineRegistryJsonAsync(CancellationToken.None);

            Assert.Equal(first, second);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UndoStopsWhenRegistryWasExternallyModified()
    {
        string registryPath = $@"Software\GpuPreferenceManager.Tests\{Guid.NewGuid():N}";
        string dataRoot = Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.Tests", Guid.NewGuid().ToString("N"));
        const string executable = @"C:\Fixture\Conflict.exe";
        try
        {
            using (RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath, writable: true))
            {
                key.SetValue(executable, "GpuPreference=1;", RegistryValueKind.String);
            }

            ApplicationDataPaths paths = ApplicationDataPaths.Create(dataRoot);
            WindowsGpuPreferenceRegistry registry = new(registryPath);
            SqliteHistoryStore history = new(paths);
            RegistryBackupService backup = new(paths, $@"HKEY_CURRENT_USER\{registryPath}");
            using GpuPreferenceChangeService changes = new(registry, history, backup, () => [CreateAdapter()]);
            ChangeResult applied = await changes.ApplyPreferenceAsync(
                [executable],
                GpuPreferenceTarget.GenericHighPerformance,
                null,
                CancellationToken.None);
            await registry.WriteValueAsync(executable, "External=change;", CancellationToken.None);

            using RollbackService rollback = new(registry, history, backup);
            RollbackPreview preview = await rollback.PreviewUndoAsync(applied.TransactionId!.Value, CancellationToken.None);
            ChangeResult result = await rollback.UndoAsync(applied.TransactionId.Value, ConflictPolicy.Stop, CancellationToken.None);

            Assert.Contains(executable, preview.Conflicts);
            Assert.False(result.Succeeded);
            Assert.Equal("External=change;", (await registry.ReadValueAsync(executable, CancellationToken.None)).StringValue);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StartupRecoveryClassifiesCompletedPendingTransaction()
    {
        string registryPath = $@"Software\GpuPreferenceManager.Tests\{Guid.NewGuid():N}";
        string dataRoot = Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.Tests", Guid.NewGuid().ToString("N"));
        const string executable = @"C:\Fixture\Pending.exe";
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.Create(dataRoot);
            WindowsGpuPreferenceRegistry registry = new(registryPath);
            SqliteHistoryStore history = new(paths);
            RegistryValueState before = new(false, null, null);
            RegistryValueState after = new(true, GpuPreferenceManager.Core.Registry.RegistryDataKind.Text, "GpuPreference=2;");
            TransactionItemState item = new(executable, before, after, "Pending", null);
            long id = await history.BeginTransactionAsync("FixturePending", null, "before", [item], CancellationToken.None);
            await registry.WriteValueAsync(executable, after.StringValue!, CancellationToken.None);

            using RollbackService rollback = new(registry, history, new(paths, $@"HKEY_CURRENT_USER\{registryPath}"));
            await rollback.RecoverPendingTransactionsAsync(CancellationToken.None);

            HistoryEntry recovered = Assert.Single(await history.QueryAsync(CancellationToken.None));
            Assert.Equal(id, recovered.Id);
            Assert.Equal(TransactionStatus.Applied, recovered.Status);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private static GpuAdapterInfo CreateAdapter()
    {
        GpuAdapterId id = new(1, 0, 0x1002, 0x73EF, 0x1EFE);
        return new(
            id,
            "Fixture",
            8L << 30,
            16L << 30,
            "1002&73EF&1EFE",
            GpuAdapterRole.DiscreteOrHighPerformance,
            AdapterIdentityConfidence.UserConfirmed,
            false,
            false,
            true);
    }
}
