using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;
namespace PrintMerkez.Web.Pages.Notifications;
public class IndexModel(PrintDbContext db,NotificationProcessor processor,IConfiguration config):PageModel
{
    [BindProperty(SupportsGet=true)]public int PageNumber{get;set;}=1;
    [BindProperty(SupportsGet=true)]public NotificationState? State{get;set;}
    public string Mode=>config["Mail:Mode"]??"Preview";
    public List<UsageNotification> Notifications{get;private set;}=[];
    public int Total{get;private set;}
    public int TotalPages=>Math.Max(1,(int)Math.Ceiling(Total/25d));
    public async Task OnGetAsync()
    {
        var query=db.UsageNotifications.AsNoTracking().Include(n=>n.Policy).AsQueryable();
        if(State.HasValue)query=query.Where(n=>n.State==State);
        Total=await query.CountAsync();PageNumber=Math.Clamp(PageNumber,1,TotalPages);
        Notifications=await query.OrderByDescending(n=>n.Id).Skip((PageNumber-1)*25).Take(25).ToListAsync();
    }
    public async Task<IActionResult> OnPostEvaluateAsync()
    {
        await UsageAlertService.ReconcileCurrentAsync(db);
        TempData["Success"]="Mevcut takvim ayı değerlendirildi. Eşiği aşan kayıtlı kullanıcılar için eksik uyarılar oluşturuldu; tekrar kayıt oluşturulmadı.";
        return RedirectToPage("Index");
    }
    public async Task<IActionResult> OnPostProcessAsync()
    {
        await db.UsageNotifications.Where(n=>n.State==NotificationState.Blocked).ExecuteUpdateAsync(s=>s.SetProperty(n=>n.NextAttemptAtUtc,DateTime.UtcNow));
        var count=await processor.ProcessBatchAsync();TempData["Success"]=$"{count} kuyruk kaydı kontrol edildi. Simülasyon ve Preview modunda dışarı mail gönderilmez.";return RedirectToPage("Index");
    }
    public async Task<IActionResult> OnPostRetryAsync(long id)
    {
        await using var transaction=await db.Database.BeginTransactionAsync();
        var updated=await db.UsageNotifications.Where(n=>n.Id==id&&n.State==NotificationState.Failed)
            .ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Retry).SetProperty(n=>n.Attempts,0).SetProperty(n=>n.NextAttemptAtUtc,DateTime.UtcNow).SetProperty(n=>n.CompletedAtUtc,(DateTime?)null));
        if(updated==0)return BadRequest("Yalnızca kesin başarısız bildirimler yeniden denenebilir. Belirsiz teslimler otomatik tekrarlanmaz.");
        db.AuditEntries.Add(new AuditEntry{Entity="UsageNotification",EntityKey=id.ToString(),Action="Yeniden deneme istendi",OccurredAtUtc=DateTime.UtcNow,Actor=User.Identity?.IsAuthenticated==true?User.Identity.Name??"Kullanıcı":"Yerel geliştirme / kimlik doğrulanmadı"});await db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"]="Kesin başarısız bildirim tekrar kuyruğa alındı.";return RedirectToPage("Index");
    }
    public async Task<IActionResult> OnPostCancelAsync(long id)
    {
        await using var transaction=await db.Database.BeginTransactionAsync();
        var count=await db.UsageNotifications.Where(n=>n.Id==id&&(n.State==NotificationState.Pending||n.State==NotificationState.Retry||n.State==NotificationState.Blocked||n.State==NotificationState.Failed||n.State==NotificationState.Uncertain))
            .ExecuteUpdateAsync(s=>s.SetProperty(n=>n.State,NotificationState.Cancelled).SetProperty(n=>n.CompletedAtUtc,DateTime.UtcNow).SetProperty(n=>n.LastError,"Bildirim kullanıcı işlemiyle iptal edildi."));
        if(count==0)return BadRequest("Gönderimi devam eden veya tamamlanmış bildirim iptal edilemez.");
        db.AuditEntries.Add(new AuditEntry{Entity="UsageNotification",EntityKey=id.ToString(),Action="Bildirim iptal edildi",OccurredAtUtc=DateTime.UtcNow,Actor=User.Identity?.IsAuthenticated==true?User.Identity.Name??"Kullanıcı":"Yerel geliştirme / kimlik doğrulanmadı"});
        await db.SaveChangesAsync();await transaction.CommitAsync();TempData["Success"]="Bildirim iptal edildi; tekrar gönderilmeyecek.";return RedirectToPage("Index");
    }
    public static DateTime Local(DateTime utc)=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
}
