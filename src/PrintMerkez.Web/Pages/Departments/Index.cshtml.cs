using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Departments;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty] public DepartmentInput Input { get; set; } = new();
    public List<Department> Departments { get; private set; } = [];
    public int? EditingId { get; private set; }
    private async Task LoadAsync() => Departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).Take(500).ToListAsync();
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EditingId = id; await LoadAsync();
        if (id.HasValue)
        {
            var department = await db.Departments.FindAsync(id.Value); if (department is null) return NotFound();
            Input = new() { Name = department.Name, IsActive = department.IsActive };
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId = id; await LoadAsync(); Input.Name = Input.Name?.Trim() ?? "";
        if (await db.Departments.AnyAsync(d => d.Name == Input.Name && (!id.HasValue || d.Id != id.Value)))
            ModelState.AddModelError("Input.Name", "Bu departman adı zaten kullanılıyor.");
        if (!ModelState.IsValid) return Page();
        var department = id.HasValue ? await db.Departments.FindAsync(id.Value) : new Department();
        if (department is null) return NotFound();
        department.Name = Input.Name; department.IsActive = Input.IsActive;
        if (!id.HasValue) db.Departments.Add(department);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Kayıt kaydedilemedi. Ad başka bir işlemde kullanılmış olabilir."); return Page(); }
        TempData["Success"] = "Departman kaydedildi. Geçmiş baskıların departman bilgisi değiştirilmedi.";
        return RedirectToPage("Index");
    }
    public class DepartmentInput
    {
        [Required(ErrorMessage = "Departman adı gerekli."), StringLength(80)] public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }
}
