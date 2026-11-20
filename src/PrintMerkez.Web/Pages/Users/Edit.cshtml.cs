using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Pages.Users;

[Authorize(Policy="Admin")]
public class EditModel(PrintDbContext db) : PageModel
{
    [BindProperty] public UserEditInput Input { get; set; }=new();
    [BindProperty,StringLength(64)] public string? ChangeToken { get; set; }
    [BindProperty] public bool ConfirmDelete { get; set; }
    [BindProperty(SupportsGet=true),StringLength(160)] public string? Search { get; set; }
    [BindProperty(SupportsGet=true)] public int ListPage { get; set; }=1;
    public DirectoryUser? Person { get; private set; }
    public List<Department> Departments { get; private set; }=[];
    public List<Location> Locations { get; private set; }=[];
    public bool Editing=>Person is not null;
    private async Task LoadChoicesAsync()
    {
        Departments=await db.Departments.AsNoTracking().OrderBy(d=>d.Name).ToListAsync();
        Locations=await db.Locations.AsNoTracking().OrderBy(l=>l.Name).ToListAsync();
    }
    private static string Stamp(DirectoryUser user)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new{user.Id,user.Account,user.NormalizedAccount,user.DisplayName,user.DepartmentId,user.LocationId,
            user.Role,user.Email,user.IsActive,user.AdObjectGuid,Imported=user.AdImportedAtUtc?.Ticks,Deleted=user.DeletedAtUtc?.Ticks}))));
    private void Populate(DirectoryUser user)
    {
        Person=user;ChangeToken=Stamp(user);
        Input=new(){Account=user.Account,DisplayName=user.DisplayName,DepartmentId=user.DepartmentId,LocationId=user.LocationId,
            Email=user.Email,Role=user.Role,IsActive=user.IsActive};
    }
    private IActionResult BackToList()=>RedirectToPage("/Users/Index",new{Search,PageNumber=Math.Max(1,ListPage)});
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadChoicesAsync();
        if(id.HasValue)
        {
            var user=await db.DirectoryUsers.SingleOrDefaultAsync(u=>u.Id==id&&u.DeletedAtUtc==null);
            if(user is null)return NotFound();Populate(user);
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        await LoadChoicesAsync();
        try
        {
            await using var transaction=await db.Database.BeginTransactionAsync();
            var user=id.HasValue?await db.DirectoryUsers.SingleOrDefaultAsync(u=>u.Id==id&&u.DeletedAtUtc==null):new DirectoryUser();
            if(user is null)return NotFound();Person=id.HasValue?user:null;
            if(id.HasValue&&ChangeToken!=Stamp(user))
            {Response.StatusCode=409;ModelState.AddModelError("","Bu kullanıcı siz düzenlerken değişti. Sayfayı yeniden açıp güncel bilgileri kontrol edin.");return Page();}
            Input.Account=Input.Account?.Trim()??"";Input.DisplayName=Input.DisplayName?.Trim()??"";
            Input.Email=string.IsNullOrWhiteSpace(Input.Email)?null:Input.Email.Trim();
            var normalized=DirectoryUser.Normalize(Input.Account);
            if(Input.DepartmentId.HasValue&&!Departments.Any(d=>d.Id==Input.DepartmentId))ModelState.AddModelError("Input.DepartmentId","Departman bulunamadı.");
            if(Input.LocationId.HasValue&&!Locations.Any(l=>l.Id==Input.LocationId))ModelState.AddModelError("Input.LocationId","Lokasyon bulunamadı.");
            if(Input.Role==OrganizationRole.Manager&&!NotificationLabels.IsEmail(Input.Email))ModelState.AddModelError("Input.Email","Yönetici için geçerli bir e-posta adresi gerekli.");
            if(id.HasValue&&await db.ManagerAssignments.AnyAsync(a=>a.ManagerUserId==id&&a.IsActive&&(!Input.IsActive||Input.Role!=OrganizationRole.Manager||a.DepartmentId!=Input.DepartmentId||a.LocationId!=Input.LocationId)))
                ModelState.AddModelError("","Bu kullanıcı aktif yönetici atamasında. Önce ilgili atamayı kapatın veya başka yönetici seçin.");
            if(await db.DirectoryUsers.AnyAsync(u=>u.NormalizedAccount==normalized&&(!id.HasValue||u.Id!=id)))
                ModelState.AddModelError("Input.Account","Bu hesap kayıtlı veya arşivde; aynı hesap tekrar eklenemez.");
            if(!ModelState.IsValid)return Page();
            user.Account=Input.Account;user.NormalizedAccount=normalized;user.DisplayName=Input.DisplayName;
            user.DepartmentId=Input.DepartmentId;user.LocationId=Input.LocationId;user.Email=Input.Email;user.Role=Input.Role;user.IsActive=Input.IsActive;
            if(!id.HasValue)db.DirectoryUsers.Add(user);
            await db.SaveChangesAsync();await transaction.CommitAsync();
            TempData["Success"]=id.HasValue?"Kullanıcı bilgileri güncellendi.":"Kullanıcı eklendi. Bu kayıt oturum açma yetkisi vermez.";
            return BackToList();
        }
        catch(Exception ex) when(ex is DbUpdateException or SqliteException)
        {ModelState.AddModelError("","Kullanıcı kaydedilemedi. Bilgileri kontrol ederek tekrar deneyin.");return Page();}
    }
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        ModelState.Clear();await LoadChoicesAsync();
        try
        {
            await using var transaction=await db.Database.BeginTransactionAsync();
            var user=await db.DirectoryUsers.SingleOrDefaultAsync(u=>u.Id==id&&u.DeletedAtUtc==null);
            if(user is null)return NotFound();
            var token=ChangeToken;Populate(user);
            if(token!=Stamp(user)){Response.StatusCode=409;ModelState.AddModelError("","Bu kullanıcı bilgileri değişti. Güncel bilgileri kontrol ederek silmeyi yeniden onaylayın.");return Page();}
            if(!ConfirmDelete){ModelState.AddModelError("","Silme işlemini onaylamalısınız.");return Page();}
            if(await db.ManagerAssignments.AnyAsync(a=>a.ManagerUserId==id&&a.IsActive))
            {ModelState.AddModelError("","Bu kullanıcı aktif yönetici atamasında. Önce atamayı kapatın veya başka yönetici seçin.");return Page();}
            if(await db.UsageNotifications.AnyAsync(n=>(n.UserId==id||n.ManagerUserId==id)&&n.State==NotificationState.Sending))
            {ModelState.AddModelError("","Bu kullanıcıyla ilgili bildirim işleniyor. Tamamlandıktan sonra tekrar deneyin.");return Page();}
            user.DeletedAtUtc=DateTime.UtcNow;user.IsActive=false;
            var cancelled=await db.UsageNotifications.Where(n=>n.UserId==id&&(n.State==NotificationState.Pending||n.State==NotificationState.Retry||n.State==NotificationState.Blocked))
                .ExecuteUpdateAsync(set=>set.SetProperty(n=>n.State,NotificationState.Cancelled).SetProperty(n=>n.CompletedAtUtc,DateTime.UtcNow)
                    .SetProperty(n=>n.LastError,"Kullanıcı yönetici tarafından silindi; bildirim iptal edildi."));
            db.AuditEntries.Add(new AuditEntry{OccurredAtUtc=DateTime.UtcNow,Actor=User.Identity?.Name??"Yönetici",Entity="DirectoryUserDeletion",EntityKey=id.ToString(),
                Action="Kullanıcı listeden kaldırıldı; baskı geçmişi korundu",AfterJson=JsonSerializer.Serialize(new{CancelledNotifications=cancelled})});
            await db.SaveChangesAsync();await transaction.CommitAsync();
            TempData["Success"]="Kullanıcı listeden kaldırıldı. Baskı geçmişi korundu; AD hesabında değişiklik yapılmadı.";
            return BackToList();
        }
        catch(Exception ex) when(ex is DbUpdateException or SqliteException)
        {ModelState.AddModelError("","Kullanıcı silinemedi. İşlem tamamlanmadı; tekrar deneyin.");return Page();}
    }
}
