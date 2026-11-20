using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Tests;

public sealed class ManagementTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static HttpClient Client(AppFactory app)
    {
        var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Agent-Key", AppFactory.Key); return client;
    }
    private static PrintEventRequest Event(DateTimeOffset? time = null) => new()
    {
        EventId = Guid.NewGuid().ToString("N"), Server = "SRV-PRINT", PrinterId = 1,
        Account = " domain\\anil ", OccurredAt = time ?? DateTimeOffset.UtcNow,
        Impressions = 6, Copies = 3, Color = PrintColor.Monochrome,
        Status = PrintJobStatus.QueueCompleted, Source = EventSource.Simulation
    };
    private static async Task<Dictionary<string,string>> Form(HttpClient client, string url, Dictionary<string,string> fields)
    {
        var html = await client.GetStringAsync(url);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(fields["__RequestVerificationToken"]); return fields;
    }
    [Theory]
    [InlineData("/Departments")][InlineData("/Users")][InlineData("/Tariffs")][InlineData("/Audit")]
    public async Task ManagementPagesRender(string url)
    {
        using var app = new AppFactory(); using var client = Client(app);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }
    [Fact]
    public async Task CostsUseEventTimeDoNotMultiplyCopiesAndPreserveHistoricalSnapshots()
    {
        using var app = new AppFactory(); using var client = Client(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var department = new Department { Name = "Muhasebe" };
        var user = new DirectoryUser { Account = "DOMAIN\\anil", NormalizedAccount = DirectoryUser.Normalize("DOMAIN\\anil"), DisplayName = "Anıl", Department = department };
        db.DirectoryUsers.Add(user);
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        db.Tariffs.AddRange(
            new CostTariff { PrinterId = 1, EffectiveFromUtc = at.AddDays(-2).UtcDateTime, MonochromeKurus = 100, ColorKurus = 400 },
            new CostTariff { PrinterId = 1, EffectiveFromUtc = at.AddMinutes(5).UtcDateTime, MonochromeKurus = 200, ColorKurus = 800 },
            new CostTariff { PrinterId = 1, EffectiveFromUtc = at.AddDays(1).UtcDateTime, MonochromeKurus = 999, ColorKurus = 999 });
        await db.SaveChangesAsync();
        var request = Event(at);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/print-events", request, Json)).StatusCode);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.EventId == request.EventId);
        Assert.Equal(600L, job.CostKurus); Assert.Equal(100, job.AppliedUnitKurus);
        Assert.Equal("Muhasebe", job.DepartmentName); Assert.Equal("Anıl", job.UserDisplayName);
        department.Name = "Finans"; user.DisplayName = "Yeni ad";
        db.Tariffs.Add(new CostTariff { PrinterId = 1, EffectiveFromUtc = at.AddMinutes(-1).UtcDateTime, MonochromeKurus = 300, ColorKurus = 900 });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/print-events", request, Json)).StatusCode);
        job = await db.Jobs.AsNoTracking().SingleAsync(j => j.EventId == request.EventId);
        Assert.Equal(600L, job.CostKurus); Assert.Equal("Muhasebe", job.DepartmentName);
        request.EventId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/v1/print-events", request, Json);
        job = await db.Jobs.AsNoTracking().SingleAsync(j => j.EventId == request.EventId);
        Assert.Equal(1800L, job.CostKurus); Assert.Equal("Finans", job.DepartmentName);
    }
    [Fact]
    public async Task MissingMetadataAndCancelledJobsDoNotProduceInventedCosts()
    {
        using var app = new AppFactory(); using var client = Client(app);
        var missing = Event(); missing.Color = PrintColor.Unknown;
        var cancelled = Event(); cancelled.Status = PrintJobStatus.Cancelled;
        var noTariff = Event();
        foreach (var request in new[] { missing, cancelled, noTariff })
            Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/print-events",request,Json)).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        Assert.Equal(CostState.MissingMetadata,(await db.Jobs.SingleAsync(j=>j.EventId==missing.EventId)).CostState);
        Assert.Equal(CostState.NotChargeable,(await db.Jobs.SingleAsync(j=>j.EventId==cancelled.EventId)).CostState);
        Assert.Equal(CostState.NoTariff,(await db.Jobs.SingleAsync(j=>j.EventId==noTariff.EventId)).CostState);
        Assert.All(await db.Jobs.Where(j=>j.Server=="SRV-PRINT").ToListAsync(),j=>Assert.Null(j.CostKurus));
    }
    [Fact]
    public async Task FreeTariffIsCalculatedAsZeroAndColorUsesColorPrice()
    {
        using var app = new AppFactory(); using var client = Client(app);
        using var scope = app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        db.Tariffs.Add(new CostTariff{PrinterId=1,EffectiveFromUtc=DateTime.UtcNow.AddDays(-1),MonochromeKurus=0,ColorKurus=250}); await db.SaveChangesAsync();
        var mono=Event(); var color=Event(); color.Color=PrintColor.Color;
        await client.PostAsJsonAsync("/api/v1/print-events",mono,Json); await client.PostAsJsonAsync("/api/v1/print-events",color,Json);
        var job=await db.Jobs.SingleAsync(j=>j.EventId==mono.EventId); Assert.Equal(0L,job.CostKurus); Assert.Equal(CostState.Calculated,job.CostState);
        Assert.Equal(1500L,(await db.Jobs.SingleAsync(j=>j.EventId==color.EventId)).CostKurus);
    }
    [Fact]
    public async Task TariffAndAuditAreAppendOnly()
    {
        using var app=new AppFactory(); using var client=Client(app);
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var tariff=new CostTariff{PrinterId=1,EffectiveFromUtc=DateTime.UtcNow,MonochromeKurus=20,ColorKurus=50}; db.Tariffs.Add(tariff); await db.SaveChangesAsync();
        tariff.ColorKurus=100; await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync()); db.Entry(tariff).State=EntityState.Unchanged;
        var audit=await db.AuditEntries.FirstAsync(); audit.Actor="changed";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task DepartmentFormCreatesAuditWithSignedInAdmin()
    {
        using var app=new AppFactory(); using var client=Client(app);
        var fields=await Form(client,"/Departments",new(){["Input.Name"]="Muhasebe",["Input.IsActive"]="true"});
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Departments",new FormUrlEncodedContent(fields))).StatusCode);
        using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var department=await db.Departments.SingleAsync(d=>d.Name=="Muhasebe");
        var audit=await db.AuditEntries.SingleAsync(a=>a.Entity==nameof(Department)&&a.EntityKey==department.Id.ToString());
        Assert.Equal("admin",audit.Actor); Assert.Null(audit.BeforeJson);
        Assert.Equal("Muhasebe",JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("Name").GetString());
    }
    [Fact]
    public async Task AuditFailureRollsBackBusinessChange()
    {
        using var app=new AppFactory(); using var client=Client(app);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER audit_failure BEFORE INSERT ON AuditEntries BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            db.Departments.Add(new Department{Name="Geri alınmalı"});
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        }
        using var verifyScope=app.Services.CreateScope();
        Assert.False(await verifyScope.ServiceProvider.GetRequiredService<PrintDbContext>().Departments.AnyAsync(d=>d.Name=="Geri alınmalı"));
    }
    [Fact]
    public async Task UserAccountsAreNormalizedAndDuplicateIsRejected()
    {
        using var app=new AppFactory(); using var client=Client(app);
        var first=await Form(client,"/Users/Edit",new(){["Input.Account"]=" DOMAIN\\Anil ",["Input.DisplayName"]="Anıl",["Input.IsActive"]="true"});
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Users/Edit",new FormUrlEncodedContent(first))).StatusCode);
        var second=await Form(client,"/Users/Edit",new(){["Input.Account"]="domain\\anil",["Input.DisplayName"]="Tekrar"});
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Users/Edit",new FormUrlEncodedContent(second))).StatusCode);
        using var scope=app.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<PrintDbContext>().DirectoryUsers.ToListAsync());
    }
    [Fact]
    public async Task TariffFormRejectsInvalidValuesAndDuplicateStart()
    {
        using var app=new AppFactory(); using var client=Client(app);
        var fields=await Form(client,"/Tariffs",new(){["Input.PrinterId"]="1",["Input.EffectiveFrom"]="2026-01-01T00:00",["Input.MonochromeKurus"]="-1",["Input.ColorKurus"]="200"});
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Tariffs",new FormUrlEncodedContent(fields))).StatusCode);
        fields["Input.MonochromeKurus"]="50";
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/Tariffs",new FormUrlEncodedContent(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/Tariffs",new FormUrlEncodedContent(fields))).StatusCode);
        using var scope=app.Services.CreateScope(); var tariff=await scope.ServiceProvider.GetRequiredService<PrintDbContext>().Tariffs.SingleAsync();
        Assert.Equal(50,tariff.MonochromeKurus); Assert.Equal(new DateTime(2025,12,31,21,0,0),tariff.EffectiveFromUtc);
    }
    [Fact]
    public async Task RealDashboardExcludesSimulationByDefault()
    {
        using var app=new AppFactory(); using var client=Client(app);
        var html=await client.GetStringAsync("/"); Assert.DoesNotContain("ORNEK\\kullanici1",html);
        var demo=await client.GetStringAsync("/?Source=Simulation"); Assert.Contains("ORNEK\\kullanici1",demo);
        Assert.DoesNotContain("ORNEK\\kullanici1",await client.GetStringAsync("/Jobs?handler=Export"));
    }
    [Fact]
    public async Task LegacyDatabaseUpgradeRetainsDataAndCreatesBackup()
    {
        var file=Path.Combine(Path.GetTempPath(),$"printmerkez-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using var db=new PrintDbContext(new DbContextOptionsBuilder<PrintDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options);
            var sql=db.GetService<IMigrator>().GenerateScript("0",DatabaseBootstrap.Baseline);
            await db.Database.ExecuteSqlRawAsync(sql);
            await db.Database.ExecuteSqlRawAsync("DROP TABLE __EFMigrationsHistory; INSERT INTO Locations(Id,Name,IsPlaceholder) VALUES(7,'Eski lokasyon',0); INSERT INTO Printers(Id,Name,Model,LocationId) VALUES(9,'Eski yazıcı','Olivetti',7); INSERT INTO Jobs(Id,EventId,Server,PrinterId,LocationId,Account,OccurredAtUtc,Color,Status,Source) VALUES(42,'legacy-event','OLD-SERVER',9,7,'DOMAIN/user','2026-01-01 00:00:00',0,0,1);");
            await DatabaseBootstrap.InitializeAsync(db);
            Assert.Equal("Eski lokasyon",(await db.Locations.FindAsync(7))!.Name);
            var job=await db.Jobs.SingleAsync(); Assert.Equal(42,job.Id); Assert.Null(job.CostKurus); Assert.Equal(CostState.NotCalculated,job.CostState);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!,Path.GetFileName(file)+".pre-upgrade-*.bak"));
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
            await DatabaseBootstrap.InitializeAsync(db);
            Assert.Single(await db.Jobs.ToListAsync());
        }
        finally
        {
            foreach(var path in Directory.GetFiles(Path.GetDirectoryName(file)!,Path.GetFileName(file)+"*")) File.Delete(path);
        }
    }

    [Fact]
    public async Task UncalculatedTotalsRemainUnknownRatherThanZero()
    {
        using var app = new AppFactory(); using var client = Client(app);
        using var scope = app.Services.CreateScope();
        var model = new PrintMerkez.Web.Pages.IndexModel(scope.ServiceProvider.GetRequiredService<PrintDbContext>()) { Source = EventSource.Simulation };
        await model.OnGetAsync();
        Assert.Equal(0, model.PricedJobs); Assert.Null(model.CostKurus);
        Assert.NotEmpty(model.ByLocation); Assert.All(model.ByLocation, l => Assert.Null(l.CostKurus));
    }
}
