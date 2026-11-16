using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Tariffs;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty] public TariffInput Input { get; set; } = new();
    public List<Printer> Printers { get; private set; } = [];
    public List<CostTariff> Tariffs { get; private set; } = [];
    private async Task LoadAsync()
    {
        Printers = await db.Printers.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        Tariffs = await db.Tariffs.AsNoTracking().Include(t => t.Printer).OrderByDescending(t => t.EffectiveFromUtc).Take(200).ToListAsync();
    }
    public async Task OnGetAsync()
    {
        await LoadAsync(); Input.EffectiveFrom = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }
    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!Printers.Any(p => p.Id == Input.PrinterId)) ModelState.AddModelError("Input.PrinterId", "Kayıtlı bir yazıcı seçin.");
        var parsed = DateTime.TryParseExact(Input.EffectiveFrom, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local);
        if (!parsed || local.Year < 2000 || local.Year > 2100) ModelState.AddModelError("Input.EffectiveFrom", "2000–2100 aralığında geçerli bir tarih ve saat girin.");
        if (!ModelState.IsValid) return Page();
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
        if (await db.Tariffs.AnyAsync(t => t.PrinterId == Input.PrinterId && t.EffectiveFromUtc == utc))
        { ModelState.AddModelError("Input.EffectiveFrom", "Bu yazıcı için aynı başlangıç anında tarife var."); return Page(); }
        db.Tariffs.Add(new CostTariff { PrinterId = Input.PrinterId, EffectiveFromUtc = utc, MonochromeKurus = Input.MonochromeKurus!.Value, ColorKurus = Input.ColorKurus!.Value });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Tarife kaydedilemedi. Aynı tarihli tarife başka bir işlemde eklenmiş olabilir."); return Page(); }
        TempData["Success"] = "Yeni tarife sürümü kaydedildi. Geçmişte hesaplanan tutarlar değiştirilmedi.";
        return RedirectToPage("Index");
    }
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
    public class TariffInput
    {
        [Range(1,int.MaxValue)] public int PrinterId { get; set; }
        [Required] public string EffectiveFrom { get; set; } = "";
        [Required(ErrorMessage = "Siyah-beyaz fiyatını girin; ücretsizse 0 yazın."), Range(0,1_000_000)] public int? MonochromeKurus { get; set; }
        [Required(ErrorMessage = "Renkli fiyatını girin; ücretsizse 0 yazın."), Range(0,1_000_000)] public int? ColorKurus { get; set; }
    }
}
