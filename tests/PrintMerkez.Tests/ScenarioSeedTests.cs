using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Tests;
public class ScenarioSeedTests
{
    [Fact]
    public async Task ScenarioCoversEveryLocationAndDepartmentAndDoesNotRepeatOrOverwrite()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var department = new Department { Name = "Mevcut departman" }; db.Departments.Add(department);
        await db.SaveChangesAsync();
        var originalPrinter = await db.Printers.FindAsync(1);
        var originalName = originalPrinter!.Name;
        await ScenarioSeed.InitializeAsync(db);
        var jobs = await db.Jobs.Where(j=>j.Server==ScenarioSeed.Server).ToListAsync();
        Assert.Equal(440,jobs.Count); Assert.All(jobs,j=>Assert.Equal(EventSource.Simulation,j.Source));
        var printers = await db.Printers.Where(p=>p.Name.StartsWith("[Örnek]")).ToListAsync();
        Assert.Equal(22,printers.Count); Assert.All(printers,p=>Assert.Null(p.IpAddress));
        Assert.Equal(11,jobs.Select(j=>j.LocationId).Distinct().Count());
        var users = await db.DirectoryUsers.Where(u=>u.Account.StartsWith("DEMO")).ToListAsync();
        Assert.Equal(27,users.Count);
        foreach(var d in await db.Departments.ToListAsync()) Assert.Equal(3,users.Count(u=>u.DepartmentId==d.Id));
        Assert.All(jobs,j=>Assert.NotNull(j.DepartmentName));
        Assert.Contains(jobs,j=>j.CostState==CostState.Calculated&&j.CostKurus>0);
        Assert.Contains(jobs,j=>j.CostState==CostState.MissingMetadata);
        Assert.Contains(jobs,j=>j.CostState==CostState.NoTariff);
        Assert.Contains(jobs,j=>j.Status==PrintJobStatus.Cancelled);
        Assert.Contains(jobs,j=>j.Status==PrintJobStatus.Failed);
        Assert.Contains(jobs,j=>j.Color==PrintColor.Color);
        Assert.Equal(originalName,(await db.Printers.FindAsync(1))!.Name);
        var editedUser=users[0]; editedUser.DisplayName="Kullanıcı düzenledi"; await db.SaveChangesAsync();
        var counts = new[] { await db.Departments.CountAsync(), await db.DirectoryUsers.CountAsync(), await db.Printers.CountAsync(), await db.Tariffs.CountAsync(), await db.Jobs.CountAsync(), await db.AuditEntries.CountAsync() };
        await ScenarioSeed.InitializeAsync(db);
        Assert.Equal(counts,new[] { await db.Departments.CountAsync(), await db.DirectoryUsers.CountAsync(), await db.Printers.CountAsync(), await db.Tariffs.CountAsync(), await db.Jobs.CountAsync(), await db.AuditEntries.CountAsync() });
        Assert.Equal("Kullanıcı düzenledi",(await db.DirectoryUsers.FindAsync(editedUser.Id))!.DisplayName);
    }
    [Fact]
    public async Task ScenarioFailureRollsBackAllPartialSeedData()
    {
        using var app = new AppFactory(); using var client = app.CreateClient();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PrintDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER scenario_failure BEFORE INSERT ON Jobs WHEN NEW.Server='DEMO-SCENARIO-V1' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            await Assert.ThrowsAsync<DbUpdateException>(()=>ScenarioSeed.InitializeAsync(db));
        }
        using var verify=app.Services.CreateScope(); var check=verify.ServiceProvider.GetRequiredService<PrintDbContext>();
        Assert.Empty(await check.DirectoryUsers.ToListAsync()); Assert.Empty(await check.Departments.ToListAsync());
        Assert.Single(await check.Printers.ToListAsync());
        Assert.False(await check.AuditEntries.AnyAsync(a=>a.Entity=="DemoScenario"));
        Assert.Equal(12,await check.Jobs.CountAsync());
    }
}
