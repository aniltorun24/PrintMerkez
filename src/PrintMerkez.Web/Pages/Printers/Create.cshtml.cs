using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Printers;
public class CreateModel(PrintDbContext db) : PageModel
{
    [BindProperty] public PrinterInput Input { get; set; } = new();
    public List<Location> Locations { get; private set; } = [];
    public bool Editing { get; private set; }
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Editing = id.HasValue;
        await LoadLocations();
        if (id.HasValue)
        {
            var p = await db.Printers.FindAsync(id.Value);
            if (p is null) return NotFound();
            Input = new() { Name = p.Name, Model = p.Model, IpAddress = p.IpAddress, QueueName = p.QueueName, LocationId = p.LocationId };
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Editing = id.HasValue;
        await LoadLocations();
        Input.Name = Input.Name?.Trim() ?? "";
        Input.Model = Input.Model?.Trim() ?? "";
        Input.IpAddress = string.IsNullOrWhiteSpace(Input.IpAddress) ? null : Input.IpAddress.Trim();
        Input.QueueName = string.IsNullOrWhiteSpace(Input.QueueName) ? null : Input.QueueName.Trim();
        if (Input.IpAddress is not null && !IPAddress.TryParse(Input.IpAddress, out _)) ModelState.AddModelError("Input.IpAddress", "Geçerli bir IP adresi girin.");
        if (!Locations.Any(x => x.Id == Input.LocationId)) ModelState.AddModelError("Input.LocationId", "Envanterden bir lokasyon seçin.");
        if (await db.Printers.AnyAsync(x => x.LocationId == Input.LocationId && x.Name == Input.Name && (!id.HasValue || x.Id != id.Value))) ModelState.AddModelError("Input.Name", "Bu lokasyonda aynı adlı yazıcı var.");
        if (!ModelState.IsValid) return Page();
        var printer = id.HasValue ? await db.Printers.FindAsync(id.Value) : new Printer();
        if (printer is null) return NotFound();
        printer.Name = Input.Name; printer.Model = Input.Model; printer.IpAddress = Input.IpAddress;
        printer.QueueName = Input.QueueName; printer.LocationId = Input.LocationId;
        if (!id.HasValue) db.Printers.Add(printer);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Kayıt kaydedilemedi. Aynı adlı bir yazıcı eşzamanlı eklenmiş olabilir."); return Page(); }
        TempData["Success"] = "Yazıcı envanteri kaydedildi. Bu işlem cihazda veya baskı sunucusunda değişiklik yapmaz.";
        return RedirectToPage("Index");
    }
    private async Task LoadLocations() => Locations = await db.Locations.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    public class PrinterInput
    {
        [Required(ErrorMessage = "Yazıcı adı gerekli."), StringLength(80)] public string Name { get; set; } = "";
        [Required(ErrorMessage = "Model gerekli."), StringLength(100)] public string Model { get; set; } = "";
        [StringLength(45)] public string? IpAddress { get; set; }
        [StringLength(120)] public string? QueueName { get; set; }
        [Range(1,int.MaxValue,ErrorMessage = "Lokasyon seçin.")] public int LocationId { get; set; }
    }
}