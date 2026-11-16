using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Users;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty] public UserInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true), StringLength(160)] public string? Search { get; set; }
    public List<DirectoryUser> Users { get; private set; } = [];
    public List<Department> Departments { get; private set; } = [];
    public List<Location> Locations { get; private set; } = [];
    public int? EditingId { get; private set; }
    private async Task LoadAsync()
    {
        Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync();
        Departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        var query = db.DirectoryUsers.AsNoTracking().Include(u => u.Department).Include(u => u.Location).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search)) { var search = Search.Trim(); query = query.Where(u => u.Account.Contains(search) || u.DisplayName.Contains(search)); }
        Users = await query.OrderBy(u => u.DisplayName).Take(200).ToListAsync();
    }
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EditingId = id; await LoadAsync();
        if (id.HasValue)
        {
            var user = await db.DirectoryUsers.FindAsync(id.Value); if (user is null) return NotFound();
            Input = new() { Account = user.Account, DisplayName = user.DisplayName, DepartmentId = user.DepartmentId, LocationId = user.LocationId, Email = user.Email, Role = user.Role, IsActive = user.IsActive };
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId = id; await LoadAsync();
        Input.Account = Input.Account?.Trim() ?? ""; Input.DisplayName = Input.DisplayName?.Trim() ?? "";
        Input.Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        var normalized = DirectoryUser.Normalize(Input.Account);
        if (Input.LocationId.HasValue && !Locations.Any(l => l.Id == Input.LocationId)) ModelState.AddModelError("Input.LocationId", "Lokasyon bulunamadı.");
        if (Input.Role == OrganizationRole.Manager && !NotificationLabels.IsEmail(Input.Email)) ModelState.AddModelError("Input.Email", "Yönetici için geçerli bir e-posta adresi gerekli.");
        if (id.HasValue && await db.ManagerAssignments.AnyAsync(a => a.ManagerUserId == id && a.IsActive && (!Input.IsActive || Input.Role != OrganizationRole.Manager || a.DepartmentId != Input.DepartmentId || a.LocationId != Input.LocationId)))
            ModelState.AddModelError("", "Bu kullanıcı aktif yönetici atamasında. Önce ilgili atamayı kapatın veya başka yönetici seçin.");
        if (Input.DepartmentId.HasValue && !Departments.Any(d => d.Id == Input.DepartmentId)) ModelState.AddModelError("Input.DepartmentId", "Departman bulunamadı.");
        if (await db.DirectoryUsers.AnyAsync(u => u.NormalizedAccount == normalized && (!id.HasValue || u.Id != id.Value)))
            ModelState.AddModelError("Input.Account", "Bu hesap zaten tanımlı. Büyük/küçük harf farklılığı yeni hesap oluşturmaz.");
        if (!ModelState.IsValid) return Page();
        var user = id.HasValue ? await db.DirectoryUsers.FindAsync(id.Value) : new DirectoryUser();
        if (user is null) return NotFound();
        user.Account = Input.Account; user.NormalizedAccount = normalized; user.DisplayName = Input.DisplayName;
        user.DepartmentId = Input.DepartmentId; user.LocationId = Input.LocationId; user.Email = Input.Email; user.Role = Input.Role; user.IsActive = Input.IsActive;
        if (!id.HasValue) db.DirectoryUsers.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Hesap kaydedilemedi; aynı hesap başka bir işlemde eklenmiş olabilir."); return Page(); }
        TempData["Success"] = "Kullanıcı bilgisi kaydedildi. Bu kayıt oturum açma yetkisi vermez.";
        return RedirectToPage("Index");
    }
    public class UserInput
    {
        [Required(ErrorMessage = "Hesap gerekli."), StringLength(160)] public string Account { get; set; } = "";
        [Required(ErrorMessage = "Ad soyad gerekli."), StringLength(120)] public string DisplayName { get; set; } = "";
        public int? DepartmentId { get; set; }
        public int? LocationId { get; set; }
        [StringLength(254), EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")] public string? Email { get; set; }
        [EnumDataType(typeof(OrganizationRole))] public OrganizationRole Role { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
