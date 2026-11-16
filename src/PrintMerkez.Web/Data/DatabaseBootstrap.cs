using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace PrintMerkez.Web.Data;

// Local development only. Production still requires an explicit deployment procedure.
public static class DatabaseBootstrap
{
    public const string Baseline = "20261109083838_InitialSchema";
    public static async Task InitializeAsync(PrintDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            var signature = await SignatureAsync(connection);
            var legacy = signature != "[]" && !await HasHistoryAsync(connection);
            if (legacy)
            {
                await using var expected = new SqliteConnection("Data Source=:memory:");
                await expected.OpenAsync();
                using var command = expected.CreateCommand();
                command.CommandText = db.GetService<IMigrator>().GenerateScript("0", Baseline);
                await command.ExecuteNonQueryAsync();
                if (signature != await SignatureAsync(expected))
                    throw new InvalidOperationException("Eski veritabanı bilinen v0.1 şemasıyla eşleşmiyor. Veri silinmedi. Şema ayrıca incelenmeli.");
            }
            var pending = (await db.Database.GetPendingMigrationsAsync()).Any();
            if ((legacy || pending) && signature != "[]" && connection.DataSource != ":memory:")
            {
                var backupPath = Path.GetFullPath(connection.DataSource) + $".pre-upgrade-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
                await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString());
                await backup.OpenAsync();
                connection.BackupDatabase(backup);
            }
            if (legacy)
            {
                await using var transaction = await connection.BeginTransactionAsync();
                using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\",\"ProductVersion\") VALUES ($id,'10.0.14');";
                command.Parameters.AddWithValue("$id", Baseline);
                await command.ExecuteNonQueryAsync(); await transaction.CommitAsync();
            }
            await db.Database.MigrateAsync();
            // Existing accounts are normalized in C#, including Unicode casing rules.
            while (true)
            {
                var batch = await db.Jobs.Where(j => j.NormalizedAccount == "").OrderBy(j => j.Id).Take(500).ToListAsync();
                if (batch.Count == 0) break;
                foreach (var job in batch)
                {
                    job.NormalizedAccount = PrintMerkez.Core.DirectoryUser.Normalize(job.Account);
                    if (job.NormalizedAccount.Length == 0) job.NormalizedAccount = "UNKNOWN-JOB-" + job.Id;
                }
                await db.SaveChangesAsync();
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    private static async Task<bool> HasHistoryAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }
    private static async Task<string> SignatureAsync(SqliteConnection connection)
    {
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT GLOB 'sqlite_*' AND name NOT GLOB '__EF*' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var signature = new List<string>();
        foreach (var table in tables)
        {
            var escaped = table.Replace("'", "''");
            var columns = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info('{escaped}')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add($"{reader[1]}:{reader[2]}:{reader[3]}:{reader[4]}:{reader[5]}");
            }
            columns.Sort(StringComparer.Ordinal);
            var foreignKeys = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA foreign_key_list('{escaped}')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) foreignKeys.Add($"{reader[2]}:{reader[3]}:{reader[4]}:{reader[5]}:{reader[6]}");
            }
            foreignKeys.Sort(StringComparer.Ordinal);
            var indexes = new List<string>();
            var indexHeaders = new List<(string Name, long Unique)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA index_list('{escaped}')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) indexHeaders.Add((reader.GetString(1), reader.GetInt64(2)));
            }
            foreach (var index in indexHeaders)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA index_info('{index.Name.Replace("'", "''")}')";
                var fields = new List<string>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) fields.Add(reader.GetString(2));
                indexes.Add(index.Unique + ":" + string.Join(',', fields));
            }
            indexes.Sort(StringComparer.Ordinal);
            signature.Add(string.Join(';', indexes) + "|" + table + "|" + string.Join(';', columns) + "|" + string.Join(';', foreignKeys));
        }
        return JsonSerializer.Serialize(signature);
    }
}
