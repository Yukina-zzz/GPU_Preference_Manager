using System.IO;
using System.Runtime.CompilerServices;
using GpuPreferenceManager.App.ViewModels;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Diagnostics;
using GpuPreferenceManager.Windows.Storage;

namespace GpuPreferenceManager.App.Tests;

public sealed class MainViewModelSingleActionTests
{
    [Fact]
    public async Task SingleActionsUseExplicitChildExecutableWithoutChangingBatchSelection()
    {
        string root = Path.Combine(Path.GetTempPath(), "GpuPreferenceManager.App.Tests", Guid.NewGuid().ToString("N"));
        ApplicationDataPaths paths = ApplicationDataPaths.Create(root);
        CapturingChangeService changes = new();
        CapturingIgnoredStore ignored = new();
        FixtureInventory inventory = new();
        MainViewModel viewModel = new(
            inventory,
            changes,
            new EmptyHistoryStore(),
            new UnusedRollbackService(),
            ignored,
            new SettingsService(paths),
            new DiagnosticsExportService(paths, new EmptyRegistryReader(), new EmptyAdapterEnumerator()),
            new AppSettings());
        try
        {
            viewModel.Adapters.Add(Adapter(GpuAdapterRole.IntegratedOrPowerSaving, "1002&164E&164E1002", 1));
            viewModel.Adapters.Add(Adapter(GpuAdapterRole.DiscreteOrHighPerformance, "1002&73EF&1EFE", 2));
            ApplicationRowViewModel row = new() { ExecutablePath = @"C:\Fixture\Host.exe" };
            const string helperPath = @"C:\Fixture\Helper.exe";

            await viewModel.ApplySinglePreferenceCommand.ExecuteAsync(new(
                row,
                helperPath,
                SinglePreferenceAction.SpecificHighPerformance));

            Assert.Equal([helperPath], changes.Paths);
            Assert.Equal(GpuPreferenceTarget.SpecificAdapter, changes.Target);
            Assert.Equal("1002&73EF&1EFE", changes.Key);
            Assert.False(row.IsSelected);

            await viewModel.SetSingleIgnoredCommand.ExecuteAsync(new(row, true));

            Assert.Equal(row.ExecutablePath, ignored.LastPath);
            Assert.True(ignored.LastIgnored);
            Assert.False(row.IsSelected);
        }
        finally
        {
            await viewModel.DisposeAsync();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static GpuAdapterInfo Adapter(GpuAdapterRole role, string key, uint luid) => new(
        new(luid, 0, 0x1002, luid, luid),
        $"Adapter {luid}",
        1L << 30,
        16L << 30,
        key,
        role,
        AdapterIdentityConfidence.UserConfirmed,
        false,
        false,
        true);

    private sealed class CapturingChangeService : IGpuPreferenceChangeService
    {
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public GpuPreferenceTarget Target { get; private set; }
        public string? Key { get; private set; }

        public Task<ChangeResult> ApplyPreferenceAsync(
            IReadOnlyList<string> executablePaths,
            GpuPreferenceTarget target,
            string? specificAdapterKey,
            CancellationToken cancellationToken)
        {
            Paths = executablePaths;
            Target = target;
            Key = specificAdapterKey;
            return Task.FromResult(new ChangeResult(null, TransactionStatus.Applied, [], "完成"));
        }
    }

    private sealed class CapturingIgnoredStore : IIgnoredApplicationStore
    {
        public string? LastPath { get; private set; }
        public bool LastIgnored { get; private set; }

        public Task<IReadOnlySet<string>> ReadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task SetIgnoredAsync(string executablePath, bool ignored, CancellationToken cancellationToken)
        {
            LastPath = executablePath;
            LastIgnored = ignored;
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureInventory : IApplicationInventoryService
    {
        public IReadOnlyList<GpuAdapterInfo> Adapters { get; private set; } = [];

        public void ApplyAdapterOverrides(IReadOnlyList<GpuAdapterOverride> adapterOverrides)
        {
        }

        public async IAsyncEnumerable<IReadOnlyList<ExecutableGpuUsage>> MonitorAsync(
            TimeSpan interval,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyHistoryStore : IHistoryStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureBaselineAsync(RegistrySnapshot registry, IReadOnlyList<GpuAdapterInfo> adapters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<long> BeginTransactionAsync(string operationType, string? targetAdapterKey, string registryBeforeHash, IReadOnlyList<TransactionItemState> items, CancellationToken cancellationToken) => Task.FromResult(1L);
        public Task CompleteTransactionAsync(long transactionId, TransactionStatus status, string? registryAfterHash, IReadOnlyList<TransactionItemState> items, string? note, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<HistoryEntry>> QueryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoryEntry>>([]);
        public Task<IReadOnlyList<TransactionItemState>> GetItemsAsync(long transactionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TransactionItemState>>([]);
        public Task<string?> GetBaselineRegistryJsonAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class UnusedRollbackService : IRollbackService
    {
        public Task<RollbackPreview> PreviewUndoAsync(long transactionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChangeResult> UndoAsync(long transactionId, ConflictPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChangeResult> RestoreBaselineAsync(ConflictPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChangeResult> RollbackToAsync(long transactionId, ConflictPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyRegistryReader : IUserGpuPreferencesReader
    {
        public Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RegistrySnapshot(DateTimeOffset.UnixEpoch, false, [], null));
    }

    private sealed class EmptyAdapterEnumerator : IGpuAdapterEnumerator
    {
        public IReadOnlyList<GpuAdapterDescriptor> EnumerateAdapters() => [];
    }
}
