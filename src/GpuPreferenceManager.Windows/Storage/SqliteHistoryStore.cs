using System.Globalization;
using System.Text.Json;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Core.Registry;
using Microsoft.Data.Sqlite;

namespace GpuPreferenceManager.Windows.Storage;

public sealed class SqliteHistoryStore : IHistoryStore
{
    private readonly ApplicationDataPaths _paths;
    private readonly string _connectionString;

    public SqliteHistoryStore(ApplicationDataPaths paths)
    {
        _paths = paths;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Pooling = false,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        try
        {
            await CreateSchemaAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            string corruptPath = Path.Combine(
                _paths.Root,
                $"data.corrupt.{DateTimeOffset.Now:yyyyMMdd_HHmmssfff}.db");
            File.Move(_paths.DatabasePath, corruptPath, overwrite: false);
            await CreateSchemaAsync(cancellationToken);
        }
    }

    public async Task EnsureBaselineAsync(
        RegistrySnapshot registry,
        IReadOnlyList<GpuAdapterInfo> adapters,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM baseline_snapshots;";
        long existing = (long)(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (existing > 0)
        {
            return;
        }

        string registryJson = RegistrySnapshotCodec.Serialize(registry);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO baseline_snapshots
            (created_utc, registry_json, registry_hash, adapter_json, windows_build, tool_version)
            VALUES ($created, $registry, $hash, $adapters, $windows, $version);
            """;
        insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$registry", registryJson);
        insert.Parameters.AddWithValue("$hash", RegistrySnapshotCodec.Hash(registryJson));
        insert.Parameters.AddWithValue("$adapters", JsonSerializer.Serialize(adapters));
        insert.Parameters.AddWithValue("$windows", Environment.OSVersion.VersionString);
        insert.Parameters.AddWithValue("$version", typeof(SqliteHistoryStore).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> BeginTransactionAsync(
        string operationType,
        string? targetAdapterKey,
        string registryBeforeHash,
        IReadOnlyList<TransactionItemState> items,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO transactions
            (created_utc, operation_type, target_adapter_key, status, registry_before_hash, tool_version)
            VALUES ($created, $operation, $target, 'Pending', $hash, $version);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$operation", operationType);
        command.Parameters.AddWithValue("$target", (object?)targetAdapterKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", registryBeforeHash);
        command.Parameters.AddWithValue("$version", typeof(SqliteHistoryStore).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        long id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("无法创建事务。"));
        await UpsertItemsAsync(connection, transaction, id, items, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task CompleteTransactionAsync(
        long transactionId,
        TransactionStatus status,
        string? registryAfterHash,
        IReadOnlyList<TransactionItemState> items,
        string? note,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE transactions SET status=$status, registry_after_hash=$hash, note=$note WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$hash", (object?)registryAfterHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", transactionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await UpsertItemsAsync(connection, transaction, transactionId, items, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, created_utc, operation_type, target_adapter_key, status, note FROM transactions ORDER BY id DESC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<HistoryEntry> result = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Enum.Parse<TransactionStatus>(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return result;
    }

    public async Task<IReadOnlyList<TransactionItemState>> GetItemsAsync(long transactionId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value_name, before_exists, before_kind, before_value, after_exists, after_kind, after_value, apply_status, error FROM transaction_items WHERE transaction_id=$id ORDER BY id;";
        command.Parameters.AddWithValue("$id", transactionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<TransactionItemState> result = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetString(0),
                ReadState(reader, 1),
                ReadState(reader, 4),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    public async Task<string?> GetBaselineRegistryJsonAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT registry_json FROM baseline_snapshots ORDER BY id LIMIT 1;";
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = DatabaseSchema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static RegistryValueState ReadState(SqliteDataReader reader, int offset) => new(
        reader.GetInt32(offset) != 0,
        reader.IsDBNull(offset + 1) ? null : (RegistryDataKind?)reader.GetInt32(offset + 1),
        reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2));

    private static async Task UpsertItemsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long transactionId,
        IReadOnlyList<TransactionItemState> items,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM transaction_items WHERE transaction_id=$id;";
            delete.Parameters.AddWithValue("$id", transactionId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (TransactionItemState item in items)
        {
            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO transaction_items
                (transaction_id, value_name, before_exists, before_kind, before_value, after_exists, after_kind, after_value, apply_status, error)
                VALUES ($transaction, $name, $beforeExists, $beforeKind, $beforeValue, $afterExists, $afterKind, $afterValue, $status, $error);
                """;
            insert.Parameters.AddWithValue("$transaction", transactionId);
            insert.Parameters.AddWithValue("$name", item.ValueName);
            AddStateParameters(insert, "before", item.Before);
            AddStateParameters(insert, "after", item.After);
            insert.Parameters.AddWithValue("$status", item.ApplyStatus);
            insert.Parameters.AddWithValue("$error", (object?)item.Error ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddStateParameters(SqliteCommand command, string prefix, RegistryValueState state)
    {
        command.Parameters.AddWithValue($"${prefix}Exists", state.Exists ? 1 : 0);
        command.Parameters.AddWithValue($"${prefix}Kind", state.Kind is null ? DBNull.Value : (int)state.Kind);
        command.Parameters.AddWithValue($"${prefix}Value", (object?)state.StringValue ?? DBNull.Value);
    }

    public const string DatabaseSchema = """
        PRAGMA foreign_keys=ON;
        CREATE TABLE IF NOT EXISTS schema_info(version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);
        INSERT OR IGNORE INTO schema_info(version, applied_utc) VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        CREATE TABLE IF NOT EXISTS baseline_snapshots(id INTEGER PRIMARY KEY, created_utc TEXT NOT NULL, registry_json TEXT NOT NULL, registry_hash TEXT NOT NULL, adapter_json TEXT NOT NULL, windows_build TEXT NOT NULL, tool_version TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS transactions(id INTEGER PRIMARY KEY, created_utc TEXT NOT NULL, operation_type TEXT NOT NULL, target_adapter_key TEXT NULL, status TEXT NOT NULL, note TEXT NULL, registry_before_hash TEXT NOT NULL, registry_after_hash TEXT NULL, tool_version TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS transaction_items(id INTEGER PRIMARY KEY, transaction_id INTEGER NOT NULL REFERENCES transactions(id), value_name TEXT NOT NULL, before_exists INTEGER NOT NULL, before_kind INTEGER NULL, before_value TEXT NULL, after_exists INTEGER NOT NULL, after_kind INTEGER NULL, after_value TEXT NULL, apply_status TEXT NOT NULL, error TEXT NULL);
        CREATE TABLE IF NOT EXISTS ignored_apps(normalized_path TEXT PRIMARY KEY, display_path TEXT NOT NULL, created_utc TEXT NOT NULL, note TEXT NULL);
        CREATE TABLE IF NOT EXISTS adapter_preferences(specific_adapter_key TEXT PRIMARY KEY, display_name TEXT NOT NULL, role TEXT NOT NULL, is_excluded INTEGER NOT NULL, confirmed_utc TEXT NULL);
        CREATE TABLE IF NOT EXISTS backup_files(id INTEGER PRIMARY KEY, transaction_id INTEGER NULL, file_path TEXT NOT NULL, created_utc TEXT NOT NULL, is_baseline INTEGER NOT NULL, is_pinned INTEGER NOT NULL, sha256 TEXT NOT NULL);
        """;
}
