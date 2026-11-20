using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Tests;

public sealed class UserDailyUsageTests
{
    private static async Task<DirectoryUser> AddPerson(PrintDbContext db, string account = "DOMAIN\\chart")
    {
        var user = new DirectoryUser { Account = account, NormalizedAccount = DirectoryUser.Normalize(account), DisplayName = "Grafik Kullanıcısı" };
        db.DirectoryUsers.Add(user); await db.SaveChangesAsync(); return user;
    }
    private static PrintJob Job(DateTime utc, int? total, string account = "DOMAIN\\chart", int? userId = null,
        EventSource source = EventSource.WindowsPrintService, PrintJobStatus status = PrintJobStatus.QueueCompleted) => new()
    {
        EventId = Guid.NewGuid().ToString("N"), Server = "CHART-TEST", PrinterId = 1, LocationId = 1,
        Account = account, DirectoryUserId = userId, OccurredAtUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc),
        Impressions = total, Copies = 3, ReportedDocumentPages = 1, Status = status, Source = source
    };

    [Fact]
    public async Task CountsLocalDaysCopiesAlreadyIncludedAndKeepsUnknownSeparate()
    {
        using var app = new AppFactory(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var user = await AddPerson(db);
        db.Jobs.AddRange(
            Job(new(2026,11,19,20,59,59), 3), // 19 Nov 23:59:59 Turkey
            Job(new(2026,11,19,21,0,0), 3),   // 20 Nov 00:00 Turkey
            Job(new(2026,11,20,10,0,0), null),
            Job(new(2026,11,20,20,59,59), 6),
            Job(new(2026,11,20,21,0,0), 100), // 21 Nov excluded
            Job(new(2026,11,20,10,0,0), 100, status: PrintJobStatus.Failed),
            Job(new(2026,11,20,10,0,0), 100, status: PrintJobStatus.Cancelled),
            Job(new(2026,11,20,10,0,0), 100, source: EventSource.Simulation),
            Job(new(2026,11,20,10,0,0), 100, account: "DOMAIN\\chart-other"));
        await db.SaveChangesAsync();
        var days = await new UserDailyUsage(db).ReadAsync(user, new(2026,11,18), new(2026,11,20), EventSource.WindowsPrintService, default);
        Assert.Equal(3, days.Count);
        Assert.Equal(0, days[0].Jobs); Assert.Equal(0, days[0].ConfirmedTotal);
        Assert.Equal(1, days[1].Jobs); Assert.Equal(3, days[1].ConfirmedTotal);
        Assert.Equal(3, days[2].Jobs); Assert.Equal(9, days[2].ConfirmedTotal); // no second multiplication by Copies
        Assert.Equal(1, days[2].UnknownJobs); Assert.Equal(3, days[2].WindowsPages);
        Assert.Equal(4, days.Sum(d => d.Jobs));
    }

    [Fact]
    public async Task IdentityMatchesHistoricalJobsAndOnlyUnassignedExactAccountJobs()
    {
        using var app = new AppFactory(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var user = await AddPerson(db); var other = await AddPerson(db, "DOMAIN\\other");
        var date = new DateTime(2026,11,20,10,0,0);
        db.Jobs.AddRange(Job(date, 2, account:"OLD\\account", userId:user.Id),
            Job(date, 3, account:" domain\\CHART "),
            Job(date, 99, userId:other.Id), Job(date, 99, account:"OTHER\\chart"));
        await db.SaveChangesAsync();
        var day = Assert.Single(await new UserDailyUsage(db).ReadAsync(user, date.Date, date.Date, EventSource.WindowsPrintService, default));
        Assert.Equal(2, day.Jobs); Assert.Equal(5, day.Impressions);
        Assert.Equal(1, await db.Jobs.CountAsync(j => j.DirectoryUserId == user.Id)); // reporting did not rewrite history
    }

    [Fact]
    public async Task AllUnknownIsNotZeroAndSimulationIsSeparated()
    {
        using var app = new AppFactory(); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>(); var user = await AddPerson(db);
        var date = new DateTime(2026,11,20,10,0,0);
        db.Jobs.AddRange(Job(date, null), Job(date, 15, source:EventSource.Simulation)); await db.SaveChangesAsync();
        var usage = new UserDailyUsage(db);
        var real = Assert.Single(await usage.ReadAsync(user, date.Date, date.Date, EventSource.WindowsPrintService, default));
        Assert.Null(real.ConfirmedTotal); Assert.Equal(1, real.UnknownJobs);
        var simulation = Assert.Single(await usage.ReadAsync(user, date.Date, date.Date, EventSource.Simulation, default));
        Assert.Equal(15, simulation.ConfirmedTotal);
    }

    [Theory]
    [InlineData("From=2026-11-20&To=2026-11-01")]
    [InlineData("From=2025-01-01&To=2026-11-20")]
    [InlineData("From=invalid")][InlineData("Source=99")][InlineData("Metric=99")]
    [InlineData("To=0001-01-01")]
    public async Task InvalidFiltersShowValidationAndLiveHandlerReturnsBadRequest(string filter)
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var user = await AddPerson(scope.ServiceProvider.GetRequiredService<PrintDbContext>());
        var response = await client.GetAsync($"/Users/Details/{user.Id}?{filter}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("data-live-region", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Users/Details/{user.Id}?handler=Live&{filter}")).StatusCode);
    }

    [Fact]
    public async Task UserLinksDetailsLiveFragmentsAndMissingUserWorkWithLoginProtection()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var user = await AddPerson(db);
        db.Jobs.Add(Job(new(2026,11,20,10,0,0), 3)); await db.SaveChangesAsync();
        var path = $"/Users/Details/{user.Id}?From=2026-11-19&To=2026-11-20";
        Assert.Contains($"/Users/Details/{user.Id}", await client.GetStringAsync("/Users"));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(path));
        Assert.Contains("Günlük baskı dağılımı", html); Assert.Contains("<svg", html);
        Assert.Contains("data-live-region", html); Assert.Contains("Belge / baskı işi", html);
        var fragment = WebUtility.HtmlDecode(await client.GetStringAsync(path + "&handler=Live"));
        Assert.DoesNotContain("<html", fragment); Assert.DoesNotContain("<form", fragment);
        Assert.Contains("data-usage-day=\"2026-11-20\"", fragment);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Users/Details/2147483647")).StatusCode);
        using var anonymousApp = new AppFactory(false);
        using var anonymous = anonymousApp.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path + "&handler=Live")).StatusCode);
    }
}
