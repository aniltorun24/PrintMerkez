using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Services;
namespace PrintMerkez.Web.Data;

// Synthetic scenarios only; never submits print jobs or changes physical devices.
public static class ScenarioSeed
{
    public const string Server = "DEMO-SCENARIO-V1";
    public const string Version = "scenario-v1";
    public static async Task InitializeAsync(PrintDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.AuditEntries.AnyAsync(a => a.Entity == "DemoScenario" && a.EntityKey == Version)) return;
        var locations = await db.Locations.OrderBy(l => l.Id).ToListAsync();
        if (locations.Count == 0) return;
        string[] names = ["Muhasebe", "Finans", "İnsan Kaynakları", "Bilgi İşlem", "Satın Alma", "Operasyon", "Teknik Hizmetler", "Yönetim"];
        var departments = await db.Departments.ToListAsync();
        foreach (var name in names)
        {
            var demoName = "[Örnek] " + name;
            if (departments.Any(d => d.Name == demoName)) continue;
            var department = new Department { Name = demoName };
            db.Departments.Add(department); departments.Add(department);
        }
        await db.SaveChangesAsync();
        var users = new List<DirectoryUser>();
        DirectoryUser? inactiveExample = null;
        foreach (var department in departments.OrderBy(d => d.Id))
        {
            for (var n = 1; n <= 3; n++)
            {
                var account = $"DEMO\\d{department.Id:000}u{n:00}";
                var normalized = DirectoryUser.Normalize(account);
                var user = await db.DirectoryUsers.SingleOrDefaultAsync(u => u.NormalizedAccount == normalized);
                if (user is null)
                {
                    user = new DirectoryUser { Account = account, NormalizedAccount = normalized,
                        DisplayName = $"[Örnek] Kullanıcı {department.Id:00}-{n:00}", DepartmentId = department.Id };
                    db.DirectoryUsers.Add(user);
                    if (n == 3 && inactiveExample is null) inactiveExample = user;
                }
                users.Add(user);
            }
        }
        var printers = new List<Printer>();
        for (var l = 0; l < locations.Count; l++)
        {
            for (var n = 0; n < 2; n++)
            {
                var location = locations[l];
                var name = $"[Örnek] {location.Name} / Yazıcı {(n == 0 ? "A" : "B")}";
                var printer = await db.Printers.SingleOrDefaultAsync(p => p.LocationId == location.Id && p.Name == name);
                if (printer is null)
                {
                    var brand = new[] { "Canon", "Kyocera", "Olivetti" }[(l + n) % 3];
                    printer = new Printer { Name = name, Model = $"{brand} — örnek cihaz, model doğrulanmadı", LocationId = location.Id };
                    db.Printers.Add(printer);
                }
                printers.Add(printer);
            }
        }
        await db.SaveChangesAsync();
        // A genuine update of a newly created example record exercises audit history.
        if (inactiveExample is not null) inactiveExample.IsActive = false;
        await db.SaveChangesAsync();
        var anchor = DateTime.UtcNow.Date.AddDays(-40);
        var tariffCount = 0;
        for (var p = 0; p < printers.Count - 1; p++)
        {
            foreach (var version in new[] { 0, 1 })
            {
                var effective = anchor.AddDays(version == 0 ? -1 : 20);
                var printerId = printers[p].Id;
                if (await db.Tariffs.AnyAsync(t => t.PrinterId == printerId && t.EffectiveFromUtc == effective)) continue;
                db.Tariffs.Add(new CostTariff { PrinterId = printerId, EffectiveFromUtc = effective,
                    MonochromeKurus = 30 + p * 2 + version * 10, ColorKurus = 120 + p * 5 + version * 25 });
                tariffCount++;
            }
        }
        await db.SaveChangesAsync();
        var existing = (await db.Jobs.Where(j => j.Server == Server).Select(j => j.EventId).ToListAsync()).ToHashSet();
        var jobCount = 0;
        for (var p = 0; p < printers.Count; p++)
        {
            for (var n = 0; n < 20; n++)
            {
                var eventId = $"{Version}-printer-{printers[p].Id}-job-{n:00}";
                if (existing.Contains(eventId)) continue;
                var user = users[(p * 20 + n) % users.Count];
                var copies = (n % 3) + 1;
                var job = new PrintJob
                {
                    EventId = eventId, Server = Server, PrinterId = printers[p].Id, LocationId = printers[p].LocationId,
                    Account = user.Account, OccurredAtUtc = anchor.AddDays(n * 2).AddMinutes(p * 11 + n),
                    Impressions = n % 11 == 0 ? null : ((n % 9) + 1) * copies,
                    Copies = n % 13 == 0 ? null : copies,
                    Color = n % 7 == 0 ? PrintColor.Unknown : n % 3 == 0 ? PrintColor.Color : PrintColor.Monochrome,
                    Status = n % 10 == 8 ? PrintJobStatus.Cancelled : n % 10 == 9 ? PrintJobStatus.Failed : PrintJobStatus.QueueCompleted,
                    Source = EventSource.Simulation
                };
                await CostCalculator.EnrichAsync(job, db);
                db.Jobs.Add(job); jobCount++;
            }
        }
        await db.SaveChangesAsync();
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = DateTime.UtcNow, Actor = "Sistem başlangıcı / örnek veri", Entity = "DemoScenario", EntityKey = Version,
            Action = "Örnek veri yüklendi", AfterJson = JsonSerializer.Serialize(new { Locations = locations.Count, Departments = departments.Count, Users = users.Count, Printers = printers.Count, TariffsAdded = tariffCount, JobsAdded = jobCount })
        });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
}
