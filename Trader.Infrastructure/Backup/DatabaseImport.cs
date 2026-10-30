using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Trader.Infrastructure.Backup;

public static class DatabaseImport
{
    private const string PendingSuffix = ".import";
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm"];

    public static string PendingPath(string databasePath) => databasePath + PendingSuffix;

    public static async Task<string?> ValidateAsync(string file, IReadOnlySet<string> knownMigrations, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = new($"Data Source={file};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(cancellationToken);

            if (await ScalarAsync(connection, "PRAGMA integrity_check", cancellationToken) is not "ok")
            {
                return "O ficheiro está danificado (a verificação de integridade falhou).";
            }

            if (await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('__EFMigrationsHistory', 'Articles', 'AspNetUsers')", cancellationToken) is not 3L)
            {
                return "Este ficheiro não é uma base de dados do Trader.";
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!knownMigrations.Contains(reader.GetString(0)))
                {
                    return "Esta base de dados foi criada por uma versão mais recente do Trader. Atualiza o servidor primeiro.";
                }
            }

            return null;
        }
        catch (SqliteException)
        {
            return "O ficheiro não é uma base de dados SQLite válida.";
        }
    }

    public static void DeleteSidecars(string file)
    {
        foreach (string suffix in SidecarSuffixes)
        {
            File.Delete(file + suffix);
        }
    }

    public static bool ApplyPending(string databasePath, string backupDirectory, ILogger? logger)
    {
        string pending = PendingPath(databasePath);

        if (!File.Exists(pending))
        {
            return false;
        }

        if (File.Exists(databasePath))
        {
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, $"before-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
            BackupCurrent(databasePath, backup);
            logger?.LogInformation("Current database saved to {Path} before importing", backup);
        }

        DeleteSidecars(databasePath);
        File.Move(pending, databasePath, overwrite: true);
        DeleteSidecars(pending);
        logger?.LogInformation("Imported database applied");
        return true;
    }

    private static void BackupCurrent(string databasePath, string backup)
    {
        try
        {
            using SqliteConnection connection = new($"Data Source={databasePath};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path";
            command.Parameters.AddWithValue("$path", backup);
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            File.Copy(databasePath, backup, overwrite: true);
        }
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
