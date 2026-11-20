using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Managers;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty] public AssignmentInput Input { get; set; }=new();
    public int? EditingId { get; private set; }
    public List<Department> Departments { get; private set; }=[];
    public List<Location> Locations { get; private set; }=[];
    public List<DirectoryUser> Managers { get; private set; }=[];
    public List<ManagerAssignment> Assignments { get; private set; }=[];
    private async Task LoadAsync()
    {
        Departments=await db.Departments.AsNoTracking().OrderBy(d=>d.Name).ToListAsync();
        Locations=await db.Locations.AsNoTracking().OrderBy(l=>l.Name).ToListAsync();
        Managers=await db.DirectoryUsers.AsNoTracking().Where(u=>u.Role==OrganizationRole.Manager&&u.IsActive&&u.DeletedAtUtc==null).OrderBy(u=>u.DisplayName).ToListAsync();
        Assignments=await db.ManagerAssignments.AsNoTracking().Include(a=>a.Department).Include(a=>a.Location).Include(a=>a.ManagerUser).OrderBy(a=>a.Department.Name).ThenBy(a=>a.Location.Name).Take(500).ToListAsync();
    }
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EditingId=id;await LoadAsync();
        if(id.HasValue){var a=await db.ManagerAssignments.FindAsync(id.Value);if(a is null)return NotFound();Input=new(){DepartmentId=a.DepartmentId,LocationId=a.LocationId,ManagerUserId=a.ManagerUserId,IsActive=a.IsActive};}
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId=id;await LoadAsync();
        if(!Departments.Any(d=>d.Id==Input.DepartmentId))ModelState.AddModelError("Input.DepartmentId","Departman bulunamadı.");
        if(!Locations.Any(l=>l.Id==Input.LocationId))ModelState.AddModelError("Input.LocationId","Lokasyon bulunamadı.");
        var manager=Managers.SingleOrDefault(u=>u.Id==Input.ManagerUserId);
        if(Input.IsActive&&(manager is null||manager.DepartmentId!=Input.DepartmentId||manager.LocationId!=Input.LocationId||!NotificationLabels.IsEmail(manager.Email)))
            ModelState.AddModelError("Input.ManagerUserId","Aktif, geçerli e-postalı ve seçilen departman/lokasyona bağlı bir yönetici seçin.");
        if(!await db.DirectoryUsers.AnyAsync(u=>u.Id==Input.ManagerUserId&&u.DeletedAtUtc==null))ModelState.AddModelError("Input.ManagerUserId","Kullanıcı bulunamadı.");
        if(await db.ManagerAssignments.AnyAsync(a=>a.DepartmentId==Input.DepartmentId&&a.LocationId==Input.LocationId&&(!id.HasValue||a.Id!=id.Value)))ModelState.AddModelError("","Bu departman ve lokasyon için atama var. Mevcut kaydı düzenleyin.");
        if(!ModelState.IsValid)return Page();
        var assignment=id.HasValue?await db.ManagerAssignments.FindAsync(id.Value):new ManagerAssignment();if(assignment is null)return NotFound();
        assignment.DepartmentId=Input.DepartmentId;assignment.LocationId=Input.LocationId;assignment.ManagerUserId=Input.ManagerUserId;assignment.IsActive=Input.IsActive;
        if(!id.HasValue)db.ManagerAssignments.Add(assignment);
        try{await db.SaveChangesAsync();}catch(DbUpdateException){ModelState.AddModelError("","Atama kaydedilemedi. Aynı kapsamda başka kayıt oluşmuş olabilir.");return Page();}
        TempData["Success"]="Yönetici ataması kaydedildi. Bekleyen bildirimler gönderim öncesinde tekrar doğrulanır.";return RedirectToPage("Index");
    }
    public class AssignmentInput
    {
        [Range(1,int.MaxValue)]public int DepartmentId{get;set;}
        [Range(1,int.MaxValue)]public int LocationId{get;set;}
        [Range(1,int.MaxValue)]public int ManagerUserId{get;set;}
        public bool IsActive{get;set;}=true;
    }
}
