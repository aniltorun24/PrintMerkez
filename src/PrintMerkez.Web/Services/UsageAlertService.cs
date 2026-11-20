using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Services;

public sealed record UsageWindow(DateTime StartUtc, DateTime EndUtc, string Key, string Label)
{
    public static UsageWindow For(DateTime utc, UsagePeriod period, long jobId = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),zone);
        var start = period == UsagePeriod.Monthly ? new DateTime(local.Year,local.Month,1) : local.Date;
        var end = period == UsagePeriod.Monthly ? start.AddMonths(1) : start.AddDays(1);
        var key = period switch { UsagePeriod.Monthly => $"month:{start:yyyy-MM}", UsagePeriod.Daily => $"day:{start:yyyy-MM-dd}", _ => $"job:{jobId}" };
        var label = period switch { UsagePeriod.Monthly => start.ToString("MM.yyyy"), UsagePeriod.Daily => start.ToString("dd.MM.yyyy"), _ => $"İş #{jobId}" };
        return new(TimeZoneInfo.ConvertTimeToUtc(start,zone),TimeZoneInfo.ConvertTimeToUtc(end,zone),key,label);
    }
}
public static class ManagerResolver
{
    public static async Task<(DirectoryUser? Manager,string? Reason)> ResolveAsync(DirectoryUser user, PrintDbContext db)
    {
        if (!user.IsActive || user.DeletedAtUtc is not null) return (null,"Kullanıcı pasif veya silinmiş.");
        if (user.DepartmentId is null || user.LocationId is null) return (null,"Kullanıcının departman veya lokasyonu tanımlı değil.");
        var assignment = await db.ManagerAssignments.AsNoTracking().Include(a=>a.ManagerUser).Include(a=>a.Department)
            .SingleOrDefaultAsync(a=>a.DepartmentId==user.DepartmentId && a.LocationId==user.LocationId && a.IsActive);
        if (assignment is null) return (null,"Bu departman ve lokasyon için aktif yönetici ataması yok.");
        var manager=assignment.ManagerUser;
        if (!assignment.Department.IsActive || !manager.IsActive || manager.DeletedAtUtc is not null || manager.Role!=OrganizationRole.Manager
            || manager.DepartmentId!=user.DepartmentId || manager.LocationId!=user.LocationId)
            return (null,"Atanan yöneticinin rolü, durumu veya organizasyon bilgisi atamayla uyuşmuyor.");
        if (manager.Id==user.Id) return (null,"Kişinin kendisine yönetici bildirimi gönderilmez.");
        if (!NotificationLabels.IsEmail(manager.Email)) return (null,"Yöneticinin geçerli e-posta adresi yok.");
        return (manager,null);
    }
}
public static class UsageAlertService
{
    public static async Task EvaluateJobAsync(PrintJob job,PrintDbContext db)
    {
        if(job.Status!=PrintJobStatus.QueueCompleted || job.Impressions is null) return;
        var user=await DirectoryAccounts.FindAsync(job.Account,db);
        if(user is null) return;
        var policies=await db.UsageAlertPolicies.Where(p=>p.IsActive&&p.Source==job.Source).ToListAsync();
        foreach(var policy in policies) await EvaluateAsync(user,policy,UsageWindow.For(job.OccurredAtUtc,policy.Period,job.Id),db,job);
    }
    public static async Task ReconcileCurrentAsync(PrintDbContext db,EventSource? source=null)
    {
        await using var transaction=await db.Database.BeginTransactionAsync();
        var policies=await db.UsageAlertPolicies.Where(p=>p.IsActive&&p.Period!=UsagePeriod.PerJob&&(!source.HasValue||p.Source==source)).ToListAsync();
        var users=await db.DirectoryUsers.Where(u=>u.DeletedAtUtc==null).Include(u=>u.Department).Include(u=>u.Location).ToListAsync();
        foreach(var policy in policies)
            foreach(var user in users) await EvaluateAsync(user,policy,UsageWindow.For(DateTime.UtcNow,policy.Period),db);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    private static async Task EvaluateAsync(DirectoryUser user,UsageAlertPolicy policy,UsageWindow window,PrintDbContext db,PrintJob? job=null)
    {
        var exists=await db.UsageNotifications.AnyAsync(n=>n.PolicyId==policy.Id&&n.UserId==user.Id&&n.PeriodKey==window.Key&&n.Source==policy.Source);
        if(exists || db.UsageNotifications.Local.Any(n=>n.PolicyId==policy.Id&&n.UserId==user.Id&&n.PeriodKey==window.Key&&n.Source==policy.Source)) return;
        long total;
        if(policy.Period==UsagePeriod.PerJob) total=job?.Impressions??0;
        else
        {
        var aliases=DirectoryAccounts.Aliases(user);
        total=await db.Jobs.Where(j=>(j.DirectoryUserId==user.Id||(j.DirectoryUserId==null&&aliases.Contains(j.NormalizedAccount)))
            &&j.Source==policy.Source&&j.Status==PrintJobStatus.QueueCompleted&&j.OccurredAtUtc>=window.StartUtc&&j.OccurredAtUtc<window.EndUtc)
            .SumAsync(j=>(long?)j.Impressions)??0;
        }
        if(total<=policy.Threshold) return;
        var resolution=await ManagerResolver.ResolveAsync(user,db);
        var notification=new UsageNotification
        {
            PolicyId=policy.Id,UserId=user.Id,PeriodKey=window.Key,PeriodLabel=window.Label+" ("+policy.Period.Display()+")",Source=policy.Source,
            DepartmentId=user.DepartmentId,LocationId=user.LocationId,
            TotalImpressions=total,Threshold=policy.Threshold,Account=user.Account,DisplayName=user.DisplayName,
            DepartmentName=user.Department?.Name??"Tanımlanmadı",LocationName=user.Location?.Name??"Tanımlanmadı",
            RecipientEmail=resolution.Manager?.Email,ManagerUserId=resolution.Manager?.Id,
            State=resolution.Manager is null?NotificationState.Blocked:NotificationState.Pending,
            LastError=resolution.Reason,CreatedAtUtc=DateTime.UtcNow,NextAttemptAtUtc=DateTime.UtcNow
        };
        notification.Subject=(policy.Source==EventSource.Simulation?"[SİMÜLASYON] ":"")+"Baskı eşiği aşıldı — "+user.DisplayName.Replace('\r',' ').Replace('\n',' ');
        notification.Body=BuildBody(notification);
        db.UsageNotifications.Add(notification);
    }
    public static string BuildBody(UsageNotification n) => $"Kullanıcı: {n.DisplayName} ({n.Account})\nDepartman: {n.DepartmentName}\nLokasyon: {n.LocationName}\nDönem: {n.PeriodLabel}\nBildirilen toplam baskı yüzü: {n.TotalImpressions}\nUyarı eşiği: {n.Threshold}\n\nBu bir bilgilendirme mesajıdır; baskı engellenmemiştir. Kopyalar toplam adede dahildir. Yalnızca kuyrukta tamamlanan ve adedi bilinen işler sayılır; fiziksel çıktı mutabakatı değildir.";
}
