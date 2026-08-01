using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Storage;

namespace GpuPreferenceManager.Windows.History;

public sealed class GpuPreferenceChangeService : IGpuPreferenceChangeService, IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IGpuPreferenceRegistry _registry;
    private readonly IHistoryStore _history;
    private readonly RegistryBackupService _backup;
    private readonly Func<IReadOnlyList<GpuAdapterInfo>> _adapters;

    public GpuPreferenceChangeService(
        IGpuPreferenceRegistry registry,
        IHistoryStore history,
        RegistryBackupService backup,
        Func<IReadOnlyList<GpuAdapterInfo>> adapters)
    {
        _registry = registry;
        _history = history;
        _backup = backup;
        _adapters = adapters;
    }

    public async Task<ChangeResult> ApplyPreferenceAsync(
        IReadOnlyList<string> executablePaths,
        GpuPreferenceTarget target,
        string? specificAdapterKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executablePaths);
        if (executablePaths.Count == 0)
        {
            return new(null, TransactionStatus.Failed, [], "没有选择应用。 ");
        }

        if (target == GpuPreferenceTarget.SpecificAdapter
            && (string.IsNullOrWhiteSpace(specificAdapterKey)
                || !_adapters().Any(adapter => adapter.IsAssignable
                    && SpecificAdapterKey.Matches(adapter.SpecificAdapterKey, specificAdapterKey))))
        {
            return new(null, TransactionStatus.Failed, [], "特定适配器不存在、不可分配或身份存在歧义。");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            RegistrySnapshot beforeSnapshot = await _registry.ReadSnapshotAsync(cancellationToken);
            await _history.EnsureBaselineAsync(beforeSnapshot, _adapters(), cancellationToken);
            List<TransactionItemState> items = [];
            foreach (string path in executablePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(path, "DirectXUserGlobalSettings", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("全局设置在第一版中只读。");
                }

                RegistryValueState before = await _registry.ReadValueAsync(path, cancellationToken);
                if (before.Exists && before.Kind != RegistryDataKind.Text)
                {
                    return new(
                        null,
                        TransactionStatus.Failed,
                        [],
                        $"拒绝修改 {path}：现有注册表值类型为 {before.Kind}，仅支持无损修改 REG_SZ。原值未改变。");
                }

                GpuPreferenceRule source = RegistryRuleParser.Parse(before.Exists ? before.StringValue : null);
                string afterValue = target switch
                {
                    GpuPreferenceTarget.WindowsDecides => RegistryRuleSerializer.ClearGpuPreference(source),
                    GpuPreferenceTarget.GenericPowerSaving => RegistryRuleSerializer.SetGenericPreference(source, GpuPreferenceKind.GenericPowerSaving),
                    GpuPreferenceTarget.GenericHighPerformance => RegistryRuleSerializer.SetGenericPreference(source, GpuPreferenceKind.GenericHighPerformance),
                    GpuPreferenceTarget.SpecificAdapter => RegistryRuleSerializer.SetSpecificAdapter(source, specificAdapterKey!),
                    _ => throw new ArgumentOutOfRangeException(nameof(target)),
                };
                RegistryValueState after = afterValue.Length == 0
                    ? new(false, null, null)
                    : new(true, RegistryDataKind.Text, afterValue);
                items.Add(new(path, before, after, "Pending", null));
            }

            await _backup.ExportAsync(beforeSnapshot, "Before_Pending", cancellationToken);
            long transactionId = await _history.BeginTransactionAsync(
                target.ToString(),
                specificAdapterKey,
                RegistrySnapshotCodec.Hash(beforeSnapshot),
                items,
                cancellationToken);
            List<TransactionItemState> applied = [];
            bool failed = false;
            foreach (TransactionItemState item in items)
            {
                try
                {
                    await ApplyStateAsync(item.ValueName, item.After, cancellationToken);
                    RegistryValueState verified = await _registry.ReadValueAsync(item.ValueName, cancellationToken);
                    if (!StatesEqual(verified, item.After))
                    {
                        throw new IOException("写后读取校验不一致。");
                    }

                    applied.Add(item with { ApplyStatus = "Applied" });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    applied.Add(item with { ApplyStatus = "Failed", Error = exception.Message });
                    failed = true;
                    break;
                }
            }

            if (failed)
            {
                await CompensateAsync(applied.Where(static item => item.ApplyStatus == "Applied").Reverse(), cancellationToken);
                foreach (TransactionItemState pending in items.Skip(applied.Count))
                {
                    applied.Add(pending with { ApplyStatus = "Skipped" });
                }
            }

            RegistrySnapshot afterSnapshot = await _registry.ReadSnapshotAsync(cancellationToken);
            TransactionStatus status = failed ? TransactionStatus.Failed : TransactionStatus.Applied;
            await _history.CompleteTransactionAsync(
                transactionId,
                status,
                RegistrySnapshotCodec.Hash(afterSnapshot),
                applied,
                failed ? "写入失败并已尝试补偿。" : "目标程序需重新启动后生效。",
                cancellationToken);
            return new(
                transactionId,
                status,
                applied,
                failed ? "写入失败；已尝试恢复修改前值。" : "已写入并校验；需重新启动目标程序后生效。");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task CompensateAsync(IEnumerable<TransactionItemState> items, CancellationToken cancellationToken)
    {
        foreach (TransactionItemState item in items)
        {
            await ApplyStateAsync(item.ValueName, item.Before, cancellationToken);
        }
    }

    private Task ApplyStateAsync(string valueName, RegistryValueState state, CancellationToken cancellationToken) =>
        state.Exists && state.Kind == RegistryDataKind.Text
            ? _registry.WriteValueAsync(valueName, state.StringValue ?? string.Empty, cancellationToken)
            : !state.Exists
                ? _registry.DeleteValueAsync(valueName, cancellationToken)
                : throw new NotSupportedException($"拒绝以 REG_SZ 覆盖 {state.Kind} 类型的值 {valueName}。");

    internal static bool StatesEqual(RegistryValueState left, RegistryValueState right) =>
        left.Exists == right.Exists
        && (!left.Exists || left.Kind == right.Kind && string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal));

    public void Dispose() => _writeLock.Dispose();
}
