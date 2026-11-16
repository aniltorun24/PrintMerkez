using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class WindowsCollectorTests
{
    [Fact]
    public async Task RealDataModeRejectsSimulationButAcceptsWindowsEvents()
    {
        using var app = new AppFactory();
        using var configured = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:AllowSimulation"] = "false" })));
        using var client = configured.CreateClient(); client.DefaultRequestHeaders.Add("X-Agent-Key", AppFactory.Key);
        var request = new PrintEventRequest { EventId = "source-policy", Server = "PRINT01", PrinterId = 1,
            Account = "realuser", OccurredAt = DateTimeOffset.UtcNow, Source = EventSource.Simulation };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/print-events", request)).StatusCode);
        request.Source = EventSource.WindowsPrintService;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/print-events", request)).StatusCode);
    }

    private sealed class FakeReader : IWindowsPrintReader
    {
        public List<CollectedWindowsEvent> Rows { get; } = [];
        public bool Offline { get; set; }
        public IReadOnlyList<CollectedWindowsEvent> Read(string server, string? bookmark, string? expectedEventId, int limit)
        {
            if (Offline) throw new IOException("offline");
            var index = bookmark is null ? -1 : Rows.FindIndex(x => x.Bookmark == bookmark);
            if (bookmark is not null && (index < 0 || WindowsPrintEventParser.Parse(Rows[index].Xml).EventId != expectedEventId))
                throw new WindowsCursorLostException();
            return Rows.Skip(index + 1).Take(limit).ToArray();
        }
    }

    private static string EventXml(int id, string queue = "Real queue", string pages = "1") => $$"""
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
          <System><Provider Name="Microsoft-Windows-PrintService"/><EventID>307</EventID><Version>0</Version>
          <TimeCreated SystemTime="{{DateTime.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ")}}"/>
          <EventRecordID>{{id}}</EventRecordID><Channel>Microsoft-Windows-PrintService/Operational</Channel>
          <Computer>PRINT01.test.invalid</Computer></System>
          <UserData><DocumentPrinted xmlns="http://manifests.microsoft.com/win/2005/08/windows/printing/spooler/core/events">
          <Param2>NEVER-COLLECT-THIS-TITLE</Param2><Param3>realuser</Param3><Param4>CLIENT</Param4>
          <Param5>{{queue}}</Param5><Param6>192.0.2.20</Param6><Param8>{{pages}}</Param8>
          </DocumentPrinted></UserData>
        </Event>
        """;

    private static WindowsCollector Collector(AppFactory app, FakeReader reader, string directory,
        int printerId, string? queue = "Real queue", string? alias = null)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WindowsCollector:Server"] = "PRINT01.test.invalid", ["WindowsCollector:Queue"] = queue,
            ["WindowsCollector:PrinterId"] = printerId.ToString(), ["WindowsCollector:CursorDirectory"] = directory
            ,["WindowsCollector:AllowedEventServers:0"] = alias
        }).Build();
        return new(reader, new(), app.Services.GetRequiredService<IServiceScopeFactory>(), settings,
            app.Services.GetRequiredService<IWebHostEnvironment>(), NullLogger<WindowsCollector>.Instance);
    }

    private static async Task<int> Printer(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var printer = new Printer { Name = "Real printer", Model = "Test", QueueName = "Real queue", LocationId = 1 };
        db.Printers.Add(printer); await db.SaveChangesAsync(); return printer.Id;
    }

    [Fact]
    public async Task ImportsRealEventsAndResumesAcrossRestartWithoutDoubleCounting()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var id = await Printer(app); var reader = new FakeReader();
            reader.Rows.Add(new(EventXml(1), "bookmark-1"));
            reader.Rows.Add(new(EventXml(2, "Other queue"), "bookmark-2"));
            var collector = Collector(app, reader, directory, id);
            await collector.PollAsync(default);
            reader.Rows.Add(new(EventXml(3, pages: "2"), "bookmark-3"));
            await Collector(app, reader, directory, id).PollAsync(default);
            // Simulate a crash before saving a cursor: replay of retained events is idempotent.
            File.Delete(collector.CursorPath);
            await collector.PollAsync(default);
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            var jobs = await db.Jobs.Where(j => j.Source == EventSource.WindowsPrintService).ToListAsync();
            Assert.Equal(2, jobs.Count); Assert.Equal(3, jobs.Sum(j => j.ReportedDocumentPages));
            Assert.All(jobs, j => { Assert.Null(j.Impressions); Assert.Null(j.Copies); Assert.Equal(PrintColor.Unknown, j.Color); Assert.Null(j.CostKurus); });
            Assert.DoesNotContain("NEVER-COLLECT-THIS-TITLE", await File.ReadAllTextAsync(collector.CursorPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RequestedColorReplayDoesNotReplaceVerifiedCountsActualColorOrPrice()
    {
        using var app=new AppFactory();using var client=app.CreateClient();
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var id=await Printer(app);var reader=new FakeReader();reader.Rows.Add(new(EventXml(1),"bookmark-1"));
            var collector=Collector(app,reader,directory,id);await collector.PollAsync(default);
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
                var job=await db.Jobs.SingleAsync(j=>j.Source==EventSource.WindowsPrintService);
                job.Copies=3;job.Impressions=3;job.Color=PrintColor.Monochrome;job.CostKurus=45;job.CostState=CostState.Calculated;
                await db.SaveChangesAsync();
            }
            File.Delete(collector.CursorPath);reader.Rows[0]=reader.Rows[0] with {RequestedColor=PrintColor.Color};
            await collector.PollAsync(default);await collector.PollAsync(default);
            using var read=app.Services.CreateScope();var database=read.ServiceProvider.GetRequiredService<PrintDbContext>();
            var stored=await database.Jobs.SingleAsync(j=>j.Source==EventSource.WindowsPrintService);
            Assert.Equal(PrintColor.Color,stored.RequestedColor);Assert.Equal(PrintColor.Monochrome,stored.Color);
            Assert.Equal(3,stored.Copies);Assert.Equal(3,stored.Impressions);Assert.Equal(45,stored.CostKurus);
            Assert.Single(await database.AuditEntries.Where(a=>a.Entity=="PrintRequestedColor").ToListAsync());
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    [Fact]
    public async Task DisconnectionAndRejectedEventsDoNotAdvanceCursor()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var id = await Printer(app); var reader = new FakeReader();
            reader.Rows.Add(new(EventXml(1), "bookmark-1"));
            var collector = Collector(app, reader, directory, id); await collector.PollAsync(default);
            var saved = await File.ReadAllTextAsync(collector.CursorPath);
            reader.Offline = true;
            await Assert.ThrowsAsync<IOException>(() => collector.PollAsync(default));
            Assert.Equal(saved, await File.ReadAllTextAsync(collector.CursorPath));
            reader.Offline = false; reader.Rows.Add(new(EventXml(2, pages: "-1"), "bookmark-2"));
            await Assert.ThrowsAsync<FormatException>(() => collector.PollAsync(default));
            Assert.Equal(saved, await File.ReadAllTextAsync(collector.CursorPath));
            reader.Rows[1] = new(EventXml(2), "bookmark-2"); await collector.PollAsync(default);
            Assert.NotEqual(saved, await File.ReadAllTextAsync(collector.CursorPath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExplicitOldServerNameSurvivesRenameButUnlistedSourceIsRejected()
    {
        using var app=new AppFactory();using var client=app.CreateClient();
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var id=await Printer(app);var reader=new FakeReader();
            reader.Rows.Add(new(EventXml(1).Replace("PRINT01.test.invalid","OLDPRINT.test.invalid"),"old"));
            reader.Rows.Add(new(EventXml(2),"new"));
            using var denied=Collector(app,reader,directory,id);
            await Assert.ThrowsAsync<WindowsCollectorConfigurationException>(()=>denied.PollAsync(default));
            using var allowed=Collector(app,reader,directory,id,alias:"OLDPRINT.test.invalid");
            await allowed.PollAsync(default);await allowed.PollAsync(default);
            using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            Assert.Equal(2,await db.Jobs.CountAsync(j=>j.Source==EventSource.WindowsPrintService));
            Assert.Equal(2,await db.Jobs.Where(j=>j.Source==EventSource.WindowsPrintService).Select(j=>j.Server).Distinct().CountAsync());
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    [Fact]
    public async Task WrongPrinterMappingCannotImportEvents()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        var reader = new FakeReader(); reader.Rows.Add(new(EventXml(1), "b1"));
        using var collector = Collector(app, reader, Path.GetTempPath(), await Printer(app), "Wrong queue");
        await Assert.ThrowsAsync<InvalidOperationException>(() => collector.PollAsync(default));
    }

    [Fact]
    public async Task LogResetWithReusedRecordNumberReplaysAndShowsGapWarning()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var id = await Printer(app); var reader = new FakeReader();
            reader.Rows.Add(new(EventXml(1), "bookmark-1"));
            var collector = Collector(app, reader, directory, id); await collector.PollAsync(default);
            reader.Rows.Clear();
            // Reuse record number and bookmark, but with a different event timestamp.
            reader.Rows.Add(new(EventXml(1).Replace("<Param8>1", "<Param8>2"), "bookmark-1"));
            await collector.PollAsync(default);
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            Assert.Equal(2, await db.Jobs.CountAsync(j => j.Source == EventSource.WindowsPrintService));
            var cursor = System.Text.Json.JsonSerializer.Deserialize<WindowsCollectorCursor>(await File.ReadAllTextAsync(collector.CursorPath));
            Assert.NotNull(cursor!.Warning);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DemoCleanupRemovesExamplesAndKeepsRealRowsAndBackup()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        await ScenarioSeed.InitializeAsync(db); await NotificationDemoSeed.InitializeAsync(db);
        db.Jobs.Add(new PrintJob { EventId = "real-kept", Server = "PRINT01", Account = "realuser", PrinterId = 1,
            LocationId = 1, Source = EventSource.WindowsPrintService, Status = PrintJobStatus.QueueCompleted,
            Impressions = 1, OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var backup = await DemoDataCleanup.BackupAsync(db, directory);
            Assert.True(new FileInfo(backup).Length > 0);
            await DemoDataCleanup.RemoveAsync(db);
            Assert.Equal(1, await db.Jobs.CountAsync());
            Assert.Equal("real-kept", (await db.Jobs.SingleAsync()).EventId);
            Assert.Equal(11, await db.Locations.CountAsync());
            Assert.False(await db.Printers.AnyAsync(p => p.Name.StartsWith("[Örnek] ")));
            Assert.False(await db.DirectoryUsers.AnyAsync(u => u.Account.StartsWith("DEMO\\")));
            Assert.Equal(0, await db.ManagerAssignments.CountAsync());
            Assert.Equal(0, await db.UsageNotifications.CountAsync());
            Assert.Equal(0, await db.Tariffs.CountAsync());
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Entity == "Maintenance"));
            Assert.False(await db.AuditEntries.AnyAsync(a => a.Entity == "DemoScenario"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
