using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Services;

public sealed class NotificationProcessor(PrintDbContext db,IUsageMailSender sender)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken=default)
    {
        var now=DateTime.UtcNow;
        await db.UsageNotifications.Where(n=>n.State==NotificationState.Sending&&n.LastAttemptAtUtc<now.AddMinutes(-5))
            .ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Uncertain).SetProperty(n=>n.LastError,"Önceki gönderim yarıda kaldı; otomatik tekrar yapılmayacak."),cancellationToken);
        var ids=await db.UsageNotifications.AsNoTracking().Where(n=>(n.State==NotificationState.Pending||n.State==NotificationState.Retry||n.State==NotificationState.Blocked)&&n.NextAttemptAtUtc<=now)
            .OrderBy(n=>n.Id).Take(10).Select(n=>n.Id).ToListAsync(cancellationToken);
        var processed=0;
        foreach(var id in ids)
        {
            var claim=Guid.NewGuid().ToString("N");
            var claimed=await db.UsageNotifications.Where(n=>n.Id==id&&(n.State==NotificationState.Pending||n.State==NotificationState.Retry||n.State==NotificationState.Blocked)&&n.NextAttemptAtUtc<=now)
                .ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Sending).SetProperty(n=>n.ClaimToken,claim).SetProperty(n=>n.LastAttemptAtUtc,now),cancellationToken);
            if(claimed==0) continue;
            var n=await db.UsageNotifications.AsNoTracking().SingleAsync(n=>n.Id==id,cancellationToken);
            var policy=await db.UsageAlertPolicies.AsNoTracking().SingleAsync(p=>p.Id==n.PolicyId,cancellationToken);
            var user=await db.DirectoryUsers.AsNoTracking().Include(u=>u.Department).Include(u=>u.Location).SingleAsync(u=>u.Id==n.UserId,cancellationToken);
            DeliveryResult result;
            if(!policy.IsActive || policy.Source!=n.Source) result=new(NotificationState.Cancelled,"Bildirim kuralı kapalı veya kaynağı değişmiş.");
            else
            {
                var resolved=await ManagerResolver.ResolveAsync(user,db);
                if(n.DepartmentId.HasValue&&n.DepartmentId!=user.DepartmentId || n.LocationId.HasValue&&n.LocationId!=user.LocationId)
                    result=new(NotificationState.Blocked,"Kullanıcının organizasyonu bildirim oluşturulduktan sonra değişti. Yanlış kapsama gönderim yapılmadı.");
                else if(resolved.Manager is null) result=new(NotificationState.Blocked,resolved.Reason);
                else
                {
                    n.ManagerUserId=resolved.Manager.Id; n.RecipientEmail=resolved.Manager.Email;
                    n.DepartmentId=user.DepartmentId; n.LocationId=user.LocationId;
                    n.DepartmentName=user.Department?.Name??"Tanımlanmadı"; n.LocationName=user.Location?.Name??"Tanımlanmadı";
                    n.Body=UsageAlertService.BuildBody(n); n.Attempts++;
                    try { result=await sender.SendAsync(n,cancellationToken); }
                    catch(Exception) { result=new(NotificationState.Uncertain,"Gönderim sonucu doğrulanamadı; otomatik tekrar yapılmayacak."); }
                }
            }
            if(result.State==NotificationState.Retry&&n.Attempts>=5) result=new(NotificationState.Failed,"Beş güvenli yeniden deneme başarısız oldu.");
            var next=DateTime.UtcNow.AddMinutes(result.State==NotificationState.Retry?Math.Min(30,Math.Pow(2,n.Attempts)):1);
            var terminal=result.State is NotificationState.Sent or NotificationState.Previewed or NotificationState.Failed or NotificationState.Uncertain or NotificationState.Cancelled;
            await db.UsageNotifications.Where(x=>x.Id==id&&x.State==NotificationState.Sending&&x.ClaimToken==claim)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,result.State).SetProperty(x=>x.LastError,result.Detail)
                    .SetProperty(x=>x.RecipientEmail,n.RecipientEmail).SetProperty(x=>x.ManagerUserId,n.ManagerUserId)
                    .SetProperty(x=>x.DepartmentId,n.DepartmentId).SetProperty(x=>x.LocationId,n.LocationId)
                    .SetProperty(x=>x.DepartmentName,n.DepartmentName).SetProperty(x=>x.LocationName,n.LocationName)
                    .SetProperty(x=>x.Body,n.Body).SetProperty(x=>x.Attempts,n.Attempts).SetProperty(x=>x.NextAttemptAtUtc,next)
                    .SetProperty(x=>x.CompletedAtUtc,terminal?(DateTime?)DateTime.UtcNow:null).SetProperty(x=>x.ClaimToken,(string?)null),cancellationToken);
            processed++;
        }
        return processed;
    }
}
public sealed class NotificationWorker(IServiceScopeFactory scopes,ILogger<NotificationWorker> logger,IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue("Notifications:WorkerEnabled",true)) return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationProcessor>().ProcessBatchAsync(stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception ex) { logger.LogError("Bildirim kuyruğu işlenemedi ({ErrorType}); sonraki turda denenecek.",ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
