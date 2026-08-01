using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.History;

public enum GpuPreferenceTarget
{
    WindowsDecides,
    GenericPowerSaving,
    GenericHighPerformance,
    SpecificAdapter,
}

public enum TransactionStatus
{
    Pending,
    Applied,
    PartiallyApplied,
    Failed,
    RolledBack,
    Superseded,
}

public enum ConflictPolicy
{
    Stop,
    Skip,
    Force,
}

public sealed record RegistryValueState(bool Exists, RegistryDataKind? Kind, string? StringValue);

public sealed record RegistryMutation(string ValueName, bool AfterExists, string? AfterValue);

public sealed record TransactionItemState(
    string ValueName,
    RegistryValueState Before,
    RegistryValueState After,
    string ApplyStatus,
    string? Error);

public sealed record HistoryEntry(
    long Id,
    DateTimeOffset CreatedUtc,
    string OperationType,
    string? TargetAdapterKey,
    TransactionStatus Status,
    string? Note);

public sealed record ChangeResult(
    long? TransactionId,
    TransactionStatus Status,
    IReadOnlyList<TransactionItemState> Items,
    string Message)
{
    public bool Succeeded => Status == TransactionStatus.Applied;
}

public sealed record RollbackPreview(
    long TransactionId,
    IReadOnlyList<TransactionItemState> Items,
    IReadOnlyList<string> Conflicts);

public interface IGpuPreferenceRegistry : IUserGpuPreferencesReader
{
    Task<RegistryValueState> ReadValueAsync(string valueName, CancellationToken cancellationToken);

    Task WriteValueAsync(string valueName, string value, CancellationToken cancellationToken);

    Task DeleteValueAsync(string valueName, CancellationToken cancellationToken);
}

public interface IHistoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task EnsureBaselineAsync(
        RegistrySnapshot registry,
        IReadOnlyList<GpuAdapterInfo> adapters,
        CancellationToken cancellationToken);

    Task<long> BeginTransactionAsync(
        string operationType,
        string? targetAdapterKey,
        string registryBeforeHash,
        IReadOnlyList<TransactionItemState> items,
        CancellationToken cancellationToken);

    Task CompleteTransactionAsync(
        long transactionId,
        TransactionStatus status,
        string? registryAfterHash,
        IReadOnlyList<TransactionItemState> items,
        string? note,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HistoryEntry>> QueryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TransactionItemState>> GetItemsAsync(long transactionId, CancellationToken cancellationToken);

    Task<string?> GetBaselineRegistryJsonAsync(CancellationToken cancellationToken);
}

public interface IGpuPreferenceChangeService
{
    Task<ChangeResult> ApplyPreferenceAsync(
        IReadOnlyList<string> executablePaths,
        GpuPreferenceTarget target,
        string? specificAdapterKey,
        CancellationToken cancellationToken);
}

public interface IRollbackService
{
    Task<RollbackPreview> PreviewUndoAsync(long transactionId, CancellationToken cancellationToken);

    Task<ChangeResult> UndoAsync(long transactionId, ConflictPolicy policy, CancellationToken cancellationToken);

    Task<ChangeResult> RestoreBaselineAsync(ConflictPolicy policy, CancellationToken cancellationToken);

    Task<ChangeResult> RollbackToAsync(long transactionId, ConflictPolicy policy, CancellationToken cancellationToken);
}
