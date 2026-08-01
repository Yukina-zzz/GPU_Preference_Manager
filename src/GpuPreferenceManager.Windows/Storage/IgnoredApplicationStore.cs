using System.Globalization;
using GpuPreferenceManager.Core.Applications;
using Microsoft.Data.Sqlite;

namespace GpuPreferenceManager.Windows.Storage;

public sealed class IgnoredApplicationStore : IIgnoredApplicationStore
{
    private readonly ApplicationDataPaths _paths;

    public IgnoredApplicationStore(ApplicationDataPaths paths) => _paths = paths;

    public async Task<IReadOnlySet<string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT normalized_path FROM ignored_apps;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async Task SetIgnoredAsync(string executablePath, bool ignored, CancellationToken cancellationToken)
    {
        string normalized = ExecutablePathNormalizer.Normalize(executablePath)
            ?? throw new ArgumentException("应用路径无效。", nameof(executablePath));
        await EnsureInitializedAsync(cancellationToken);
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        if (ignored)
        {
            command.CommandText = "INSERT OR REPLACE INTO ignored_apps(normalized_path, display_path, created_utc) VALUES($normalized, $display, $created);";
            command.Parameters.AddWithValue("$normalized", normalized);
            command.Parameters.AddWithValue("$display", executablePath);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            command.CommandText = "DELETE FROM ignored_apps WHERE normalized_path=$normalized;";
            command.Parameters.AddWithValue("$normalized", normalized);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken) =>
        await new SqliteHistoryStore(_paths).InitializeAsync(cancellationToken);

    private SqliteConnection CreateConnection() => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Pooling = false,
        }.ToString());
}
