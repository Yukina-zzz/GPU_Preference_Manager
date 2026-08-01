using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Storage;

namespace GpuPreferenceManager.Windows.History;

public sealed class RollbackService : IRollbackService, IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IGpuPreferenceRegistry _registry;
    private readonly IHistoryStore _history;
    private readonly RegistryBackupService _backup;

    public RollbackService(
        IGpuPreferenceRegistry registry,
        IHistoryStore history,
        RegistryBackupService backup)
    {
        _registry = registry;
        _history = history;
        _backup = backup;
    }

    public async Task<RollbackPreview> PreviewUndoAsync(long transactionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<TransactionItemState> items = await _history.GetItemsAsync(transactionId, cancellationToken);
        List<string> conflicts = [];
        foreach (TransactionItemState item in items)
        {
            RegistryValueState current = await _registry.ReadValueAsync(item.ValueName, cancellationToken);
            if (!GpuPreferenceChangeService.StatesEqual(current, item.After))
            {
                conflicts.Add(item.ValueName);
            }
        }

        return new(transactionId, items, conflicts);
    }

    public async Task<ChangeResult> UndoAsync(
        long transactionId,
        ConflictPolicy policy,
        CancellationToken cancellationToken)
    {
        RollbackPreview preview = await PreviewUndoAsync(transactionId, cancellationToken);
        return await ApplyHistoricalStatesAsync(
            $"Undo:{transactionId}",
            preview.Items.Select(static item => (item.ValueName, item.Before, item.After)).ToList(),
            preview.Conflicts.ToHashSet(StringComparer.OrdinalIgnoreCase),
            policy,
            cancellationToken);
    }

    public async Task<ChangeResult> RestoreBaselineAsync(
        ConflictPolicy policy,
        CancellationToken cancellationToken)
    {
        string? json = await _history.GetBaselineRegistryJsonAsync(cancellationToken);
        if (json is null)
        {
            return new(null, TransactionStatus.Failed, [], "尚未创建 baseline。");
        }

        RegistrySnapshot baseline = RegistrySnapshotCodec.Deserialize(json);
        RegistrySnapshot current = await _registry.ReadSnapshotAsync(cancellationToken);
        Dictionary<string, RegistryValueSnapshot> baselineValues = baseline.Values.ToDictionary(
            static value => value.Name,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, RegistryValueSnapshot> currentValues = current.Values.ToDictionary(
            static value => value.Name,
            StringComparer.OrdinalIgnoreCase);
        List<(string Name, RegistryValueState Target, RegistryValueState Expected)> states = [];
        foreach (string name in baselineValues.Keys.Union(currentValues.Keys, StringComparer.OrdinalIgnoreCase))
        {
            RegistryValueState target = baselineValues.TryGetValue(name, out RegistryValueSnapshot? baselineValue)
                ? ToState(baselineValue)
                : new(false, null, null);
            RegistryValueState expected = currentValues.TryGetValue(name, out RegistryValueSnapshot? currentValue)
                ? ToState(currentValue)
                : new(false, null, null);
            states.Add((name, target, expected));
        }

        return await ApplyHistoricalStatesAsync(
            "RestoreBaseline",
            states,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            policy,
            cancellationToken);
    }

    public async Task<ChangeResult> RollbackToAsync(
        long transactionId,
        ConflictPolicy policy,
        CancellationToken cancellationToken)
    {
        List<HistoryEntry> newer = (await _history.QueryAsync(cancellationToken))
            .Where(entry => entry.Id > transactionId && entry.Status == TransactionStatus.Applied)
            .OrderByDescending(static entry => entry.Id)
            .ToList();
        if (newer.Count == 0)
        {
            return new(null, TransactionStatus.Applied, [], "已经位于所选历史节点。 ");
        }

        ChangeResult? last = null;
        foreach (HistoryEntry entry in newer)
        {
            last = await UndoAsync(entry.Id, policy, cancellationToken);
            if (!last.Succeeded && policy == ConflictPolicy.Stop)
            {
                return last;
            }
        }

        return last! with { Message = $"已回滚到事务 {transactionId}；逆向操作均保留在历史中。" };
    }

    public async Task RecoverPendingTransactionsAsync(CancellationToken cancellationToken)
    {
        foreach (HistoryEntry entry in (await _history.QueryAsync(cancellationToken))
                     .Where(static entry => entry.Status == TransactionStatus.Pending))
        {
            IReadOnlyList<TransactionItemState> items = await _history.GetItemsAsync(entry.Id, cancellationToken);
            int afterCount = 0;
            int beforeCount = 0;
            foreach (TransactionItemState item in items)
            {
                RegistryValueState current = await _registry.ReadValueAsync(item.ValueName, cancellationToken);
                afterCount += GpuPreferenceChangeService.StatesEqual(current, item.After) ? 1 : 0;
                beforeCount += GpuPreferenceChangeService.StatesEqual(current, item.Before) ? 1 : 0;
            }

            TransactionStatus status = afterCount == items.Count
                ? TransactionStatus.Applied
                : beforeCount == items.Count
                    ? TransactionStatus.Failed
                    : TransactionStatus.PartiallyApplied;
            await _history.CompleteTransactionAsync(
                entry.Id,
                status,
                null,
                items,
                "启动恢复已检查注册表当前状态。",
                cancellationToken);
        }
    }

    private async Task<ChangeResult> ApplyHistoricalStatesAsync(
        string operation,
        IReadOnlyList<(string Name, RegistryValueState Target, RegistryValueState Expected)> states,
        HashSet<string> conflicts,
        ConflictPolicy policy,
        CancellationToken cancellationToken)
    {
        if (conflicts.Count > 0 && policy == ConflictPolicy.Stop)
        {
            return new(null, TransactionStatus.Failed, [], $"检测到 {conflicts.Count} 个外部修改冲突。 ");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            RegistrySnapshot before = await _registry.ReadSnapshotAsync(cancellationToken);
            await _backup.ExportAsync(before, "Before_Rollback", cancellationToken);
            List<TransactionItemState> transactionItems = states.Select(state => new TransactionItemState(
                state.Name,
                state.Expected,
                state.Target,
                conflicts.Contains(state.Name) && policy == ConflictPolicy.Skip ? "SkippedConflict" : "Pending",
                null)).ToList();
            long id = await _history.BeginTransactionAsync(
                operation,
                null,
                RegistrySnapshotCodec.Hash(before),
                transactionItems,
                cancellationToken);
            List<TransactionItemState> result = [];
            foreach (TransactionItemState item in transactionItems)
            {
                if (item.ApplyStatus == "SkippedConflict")
                {
                    result.Add(item);
                    continue;
                }

                try
                {
                    await ApplyStateAsync(item.ValueName, item.After, cancellationToken);
                    RegistryValueState verified = await _registry.ReadValueAsync(item.ValueName, cancellationToken);
                    if (!GpuPreferenceChangeService.StatesEqual(verified, item.After))
                    {
                        throw new IOException("回滚写后校验不一致。");
                    }

                    result.Add(item with { ApplyStatus = "Applied" });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result.Add(item with { ApplyStatus = "Failed", Error = exception.Message });
                }
            }

            TransactionStatus status = result.Any(static item => item.ApplyStatus == "Failed")
                ? TransactionStatus.PartiallyApplied
                : TransactionStatus.Applied;
            RegistrySnapshot after = await _registry.ReadSnapshotAsync(cancellationToken);
            await _history.CompleteTransactionAsync(
                id,
                status,
                RegistrySnapshotCodec.Hash(after),
                result,
                operation,
                cancellationToken);
            return new(id, status, result, status == TransactionStatus.Applied ? "回滚完成。" : "回滚部分完成，请检查失败项。");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private Task ApplyStateAsync(string name, RegistryValueState state, CancellationToken cancellationToken) =>
        state.Exists && state.Kind == RegistryDataKind.Text
            ? _registry.WriteValueAsync(name, state.StringValue ?? string.Empty, cancellationToken)
            : !state.Exists
                ? _registry.DeleteValueAsync(name, cancellationToken)
                : throw new NotSupportedException($"暂不支持恢复 {state.Kind} 类型的值 {name}。");

    private static RegistryValueState ToState(RegistryValueSnapshot value) =>
        new(true, value.Data.Kind, value.Data.StringValue);

    public void Dispose() => _writeLock.Dispose();
}
