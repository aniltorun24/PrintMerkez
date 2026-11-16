using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Tests;

public sealed class PrintMeasurementTests
{
    [Fact]
    public async Task DocumentPagesWithoutKnownCopiesDoNotTriggerMonthlyNotificationOrCost()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Key", AppFactory.Key);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var user = new DirectoryUser { Account = "realuser", NormalizedAccount = "REALUSER", DisplayName = "Real user" };
            db.DirectoryUsers.Add(user);
            db.UsageAlertPolicies.Add(new UsageAlertPolicy { Name = "Low real threshold", Threshold = 1, Source = EventSource.WindowsPrintService });
            await db.SaveChangesAsync();
        }
        var request = new PrintEventRequest { EventId = "raw-document-pages", Server = "PRINT01", PrinterId = 1,
            Account = "realuser", OccurredAt = DateTimeOffset.UtcNow, ReportedDocumentPages = 500,
            Impressions = null, Copies = null, Source = EventSource.WindowsPrintService, Status = PrintJobStatus.QueueCompleted };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/print-events", request)).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var job = await db.Jobs.SingleAsync(j => j.EventId == request.EventId);
            Assert.Equal(500, job.ReportedDocumentPages); Assert.Null(job.Impressions); Assert.Null(job.CostKurus);
            Assert.False(await db.UsageNotifications.AnyAsync());
        }
        var jobsHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Jobs"));
        Assert.Contains("Windows belge sayfası", jobsHtml); Assert.Contains("Doğrulanmadı", jobsHtml);
        var csv = await client.GetStringAsync("/Jobs?handler=Export");
        Assert.Contains("Windows belge sayfası", csv);
        Assert.Contains("\"Doğrulanmadı\";\"500\";\"Bilinmiyor\"", csv);
        var dashboard = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("Toplamı bilinen baskı yüzü", dashboard);
        Assert.Contains("1 kayıtta toplam doğrulanmadı", dashboard);
    }

    [Fact]
    public async Task UpgradeMovesOnlyOldCollectorMeasurementsAndPreservesOtherKnownTotals()
    {
        var path = Path.Combine(Path.GetTempPath(), $"measurement-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new PrintDbContext(new DbContextOptionsBuilder<PrintDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options);
            await db.GetService<IMigrator>().MigrateAsync("20261111083432_UsageManagerNotifications");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Locations(Id,Name,IsPlaceholder) VALUES(1,'Real location',0);
                INSERT INTO Printers(Id,Name,Model,LocationId) VALUES(1,'Real printer','Test',1);
                INSERT INTO Jobs(EventId,Server,PrinterId,LocationId,Account,NormalizedAccount,OccurredAtUtc,Color,Status,Source,Impressions,Copies,CostState)
                VALUES('win307-legacy','PRINT01',1,1,'user','USER','2026-11-13 07:00:00',0,0,1,1,NULL,2),
                      ('external-verified','PRINT01',1,1,'user','USER','2026-11-13 07:00:00',0,0,1,3,3,2),
                      ('simulation','TEST',1,1,'user','USER','2026-11-13 07:00:00',0,0,0,6,3,2);
                """);
            await db.Database.MigrateAsync();
            var old = await db.Jobs.SingleAsync(j => j.EventId == "win307-legacy");
            Assert.Null(old.Impressions); Assert.Equal(1, old.ReportedDocumentPages);
            var verified = await db.Jobs.SingleAsync(j => j.EventId == "external-verified");
            Assert.Equal(3, verified.Impressions); Assert.Null(verified.ReportedDocumentPages);
            Assert.Equal(6, (await db.Jobs.SingleAsync(j => j.EventId == "simulation")).Impressions);
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Entity == "MeasurementCorrection"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
