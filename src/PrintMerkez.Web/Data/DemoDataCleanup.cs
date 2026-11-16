using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;

namespace PrintMerkez.Web.Data;

public static class DemoDataCleanup
{
    public static async Task<string> BackupAsync(PrintDbContext db, string directory)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"before-real-data-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        await db.Database.OpenConnectionAsync();
        try
        {
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
            await backup.OpenAsync();
            ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(backup);
        }
        finally { await db.Database.CloseConnectionAsync(); }
        return destination;
    }

    public static async Task RemoveAsync(PrintDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Only simulation events are removed. Windows events and their referenced metadata survive.
        await db.UsageNotifications.Where(n => n.Source == EventSource.Simulation).ExecuteDeleteAsync();
        await db.Jobs.Where(j => j.Source == EventSource.Simulation).ExecuteDeleteAsync();
        var policies = await db.UsageAlertPolicies.Where(p => p.Source == EventSource.Simulation
            && !db.UsageNotifications.Any(n => n.PolicyId == p.Id)).Select(p => p.Id).ToListAsync();
        await db.UsageAlertPolicies.Where(p => policies.Contains(p.Id)).ExecuteDeleteAsync();
        var users = await db.DirectoryUsers.Where(u => u.Account.StartsWith("DEMO\\")
            && !db.Jobs.Any(j => j.DirectoryUserId == u.Id)
            && !db.UsageNotifications.Any(n => n.UserId == u.Id || n.ManagerUserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        var assignments = await db.ManagerAssignments.Where(a => users.Contains(a.ManagerUserId)).Select(a => a.Id).ToListAsync();
        await db.ManagerAssignments.Where(a => assignments.Contains(a.Id)).ExecuteDeleteAsync();
        await db.DirectoryUsers.Where(u => users.Contains(u.Id)).ExecuteDeleteAsync();
        var printers = await db.Printers.Where(p => (p.Name.StartsWith("[Örnek] ")
            || (p.Name == "Pilot Olivetti" && p.QueueName == null && p.IpAddress == null))
            && !db.Jobs.Any(j => j.PrinterId == p.Id)).Select(p => p.Id).ToListAsync();
        var tariffs = await db.Tariffs.Where(t => printers.Contains(t.PrinterId)
            && !db.Jobs.Any(j => j.AppliedTariffId == t.Id)).Select(t => t.Id).ToListAsync();
        await db.Tariffs.Where(t => tariffs.Contains(t.Id)).ExecuteDeleteAsync();
        await db.Printers.Where(p => printers.Contains(p.Id) && !db.Tariffs.Any(t => t.PrinterId == p.Id)).ExecuteDeleteAsync();
        var departments = await db.Departments.Where(d => d.Name.StartsWith("[Örnek] ")
            && !db.DirectoryUsers.Any(u => u.DepartmentId == d.Id)
            && !db.ManagerAssignments.Any(a => a.DepartmentId == d.Id)
            && !db.UsageNotifications.Any(n => n.DepartmentId == d.Id)).Select(d => d.Id).ToListAsync();
        await db.Departments.Where(d => departments.Contains(d.Id)).ExecuteDeleteAsync();
        // Explicit maintenance exception: remove audit rows belonging to deleted examples only.
        foreach (var group in new[] { ("Printer", printers), ("DirectoryUser", users), ("Department", departments),
            ("CostTariff", tariffs), ("UsageAlertPolicy", policies), ("ManagerAssignment", assignments) })
        {
            var keys = group.Item2.Select(id => id.ToString()).ToArray();
            await db.AuditEntries.Where(a => a.Entity == group.Item1 && keys.Contains(a.EntityKey)).ExecuteDeleteAsync();
        }
        await db.AuditEntries.Where(a => a.Entity == "DemoScenario").ExecuteDeleteAsync();
        db.AuditEntries.Add(new AuditEntry { Entity = "Maintenance", EntityKey = "RemoveDemoData",
            Action = "Örnek veriler yedeklenerek temizlendi", Actor = "Yerel bakım", OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
