using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Services;
namespace PrintMerkez.Web.Data;
public static class NotificationDemoSeed
{
    public static async Task InitializeAsync(PrintDbContext db)
    {
        const string version="notification-scenario-v1";
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {
            if(await db.AuditEntries.AnyAsync(a=>a.Entity=="DemoScenario"&&a.EntityKey==version))return;
            var locations=await db.Locations.OrderBy(l=>l.Id).ToListAsync();if(locations.Count==0)return;
            var users=await db.DirectoryUsers.Where(u=>u.Account.StartsWith("DEMO\\")&&u.Role==OrganizationRole.Employee&&u.DepartmentId!=null).OrderBy(u=>u.Id).ToListAsync();
            for(var i=0;i<users.Count;i++)if(users[i].LocationId is null)users[i].LocationId=locations[i%locations.Count].Id;
            await db.SaveChangesAsync();
            foreach(var group in users.Where(u=>u.IsActive).GroupBy(u=>new{DepartmentId=u.DepartmentId!.Value,LocationId=u.LocationId!.Value}))
            {
                var account=$"DEMO\\manager.d{group.Key.DepartmentId}.l{group.Key.LocationId}";var normalized=DirectoryUser.Normalize(account);
                var manager=await db.DirectoryUsers.SingleOrDefaultAsync(u=>u.NormalizedAccount==normalized);
                if(manager is null)
                {
                    manager=new DirectoryUser{Account=account,NormalizedAccount=normalized,DisplayName=$"[Örnek] Yönetici D{group.Key.DepartmentId} / L{group.Key.LocationId}",DepartmentId=group.Key.DepartmentId,LocationId=group.Key.LocationId,Role=OrganizationRole.Manager,Email=$"manager.d{group.Key.DepartmentId}.l{group.Key.LocationId}@example.invalid"};
                    db.DirectoryUsers.Add(manager);await db.SaveChangesAsync();
                }
                if(!await db.ManagerAssignments.AnyAsync(a=>a.DepartmentId==group.Key.DepartmentId&&a.LocationId==group.Key.LocationId))
                    db.ManagerAssignments.Add(new ManagerAssignment{DepartmentId=group.Key.DepartmentId,LocationId=group.Key.LocationId,ManagerUserId=manager.Id});
            }
            if(!await db.UsageAlertPolicies.AnyAsync(p=>p.Name=="[Örnek] Aylık yönetici uyarısı"))
                db.UsageAlertPolicies.Add(new UsageAlertPolicy{Name="[Örnek] Aylık yönetici uyarısı",Threshold=20,Period=UsagePeriod.Monthly,Source=EventSource.Simulation});
            await db.SaveChangesAsync();
            var printer=await db.Printers.OrderBy(p=>p.Id).FirstOrDefaultAsync();
            if(printer is not null)
            {
                foreach(var user in users.Where(u=>u.IsActive))
                    for(var i=0;i<2;i++)
                    {
                        var job=new PrintJob{EventId=$"{version}-user-{user.Id}-{i}",Server="DEMO-NOTIFICATION-V1",PrinterId=printer.Id,LocationId=printer.LocationId,
                            Account=user.Account,OccurredAtUtc=DateTime.UtcNow,Impressions=i==0?15:10,Copies=1,Color=PrintColor.Monochrome,Status=PrintJobStatus.QueueCompleted,Source=EventSource.Simulation};
                        await CostCalculator.EnrichAsync(job,db);db.Jobs.Add(job);
                    }
            }
            db.AuditEntries.Add(new AuditEntry{Entity="DemoScenario",EntityKey=version,Action="Örnek bildirim yapısı yüklendi",OccurredAtUtc=DateTime.UtcNow,Actor="Sistem başlangıcı / örnek veri"});
            await db.SaveChangesAsync();await transaction.CommitAsync();
        }
        await UsageAlertService.ReconcileCurrentAsync(db,EventSource.Simulation);
    }
}
