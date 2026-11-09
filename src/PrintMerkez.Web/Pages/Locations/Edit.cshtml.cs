using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Locations;
public class EditModel(PrintDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Lokasyon adı gerekli."), StringLength(80)] public string Name { get; set; } = "";
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue) { var l = await db.Locations.FindAsync(id.Value); if (l is null) return NotFound(); Name = l.Name; }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Name = Name?.Trim() ?? "";
        if (await db.Locations.AnyAsync(x => x.Name == Name && (!id.HasValue || x.Id != id.Value))) ModelState.AddModelError(nameof(Name), "Bu lokasyon adı zaten var.");
        if (!ModelState.IsValid) return Page();
        var l = id.HasValue ? await db.Locations.FindAsync(id.Value) : new Location();
        if (l is null) return NotFound();
        l.Name = Name; l.IsPlaceholder = false;
        if (!id.HasValue) db.Locations.Add(l);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Lokasyon kaydedilemedi. Aynı adla başka bir kayıt oluşmuş olabilir."); return Page(); }
        TempData["Success"] = "Lokasyon kaydedildi.";
        return RedirectToPage("Index");
    }
}