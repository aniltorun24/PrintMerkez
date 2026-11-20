using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Web.Data;

public static class ServerSnapshot
{
    public static async Task ExportAsync(PrintDbContext db, IConfiguration config, IHostEnvironment environment,
        string destination)
    {
        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (!File.Exists(source)) throw new InvalidOperationException("Kaynak veritabanı bulunamadı; boş paket oluşturulmadı.");
        Directory.CreateDirectory(destination);
        var target = Path.Combine(destination, "printmerkez.db");
        if (File.Exists(target)) throw new IOException("Yayın verisi zaten var; üzerine yazılmadı.");
        // Cursor FIRST, consistent database snapshot SECOND: replay is safe, skipping unseen events is not.
        var cursor = WindowsCollector.GetCursorPath(config, environment);
        if (File.Exists(cursor))
        {
            using var stream = new FileStream(cursor, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            await File.WriteAllTextAsync(Path.Combine(destination, "source-cursor.json"), await reader.ReadToEndAsync());
        }
        var backup = await DemoDataCleanup.BackupAsync(db, destination);
        File.Move(backup, target);
        await File.WriteAllTextAsync(Path.Combine(destination, "snapshot-utc.txt"), DateTime.UtcNow.ToString("O"));
    }
}
