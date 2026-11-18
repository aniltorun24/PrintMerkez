using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Jobs;
public class IndexModel(PrintDbContext db) : PageModel
{
    public async Task<IActionResult> OnGetLiveAsync([FromServices] PrintMerkez.Web.Services.LiveUpdates live)
    {
        var unchanged = PrintMerkez.Web.Services.LiveUpdates.NotModified(this, live);
        if (unchanged is not null) return unchanged;
        await OnGetAsync();
        if (!ModelState.IsValid) return BadRequest("Geçersiz filtre.");
        return Partial("_JobsLive", this);
    }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? LocationId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public EventSource? Source { get; set; } = EventSource.WindowsPrintService;
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public List<Location> Locations { get; private set; } = [];
    public List<PrintJob> Jobs { get; private set; } = [];
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / 25d));
    private IQueryable<PrintJob> Filter()
    {
        var query = db.Jobs.AsNoTracking().Include(j => j.Printer).Include(j => j.Location).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search)) { var s = Search.Trim(); query = query.Where(j => j.Account.Contains(s) || j.Printer.Name.Contains(s) || j.EventId.Contains(s)); }
        if (LocationId.HasValue) query = query.Where(j => j.LocationId == LocationId);
        if (Source.HasValue) query = query.Where(j => j.Source == Source);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        if (From.HasValue) { var from = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(From.Value.Date, DateTimeKind.Unspecified), zone); query = query.Where(j => j.OccurredAtUtc >= from); }
        if (To.HasValue) { var to = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(To.Value.Date.AddDays(1), DateTimeKind.Unspecified), zone); query = query.Where(j => j.OccurredAtUtc < to); }
        return query;
    }
    private void ValidateFilters()
    {
        if (From > To) ModelState.AddModelError("", "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        if (From?.Year < 2000 || To?.Year < 2000 || From?.Year > 2100 || To?.Year > 2100) ModelState.AddModelError("", "Tarih 2000–2100 aralığında olmalı.");
        if (Source.HasValue && !Enum.IsDefined(Source.Value)) ModelState.AddModelError("", "Geçersiz kayıt kaynağı.");
    }
    public async Task OnGetAsync()
    {
        Locations = await db.Locations.OrderBy(x => x.Name).AsNoTracking().ToListAsync();
        ValidateFilters();
        if (!ModelState.IsValid) return;
        var query = Filter(); Total = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber,1,TotalPages);
        Jobs = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Skip((PageNumber-1)*25).Take(25).ToListAsync();
    }
    public async Task<IActionResult> OnGetExportAsync()
    {
        ValidateFilters();
        if (!ModelState.IsValid) return BadRequest("Filtreler geçersiz.");
        var query = Filter();
        if (await query.CountAsync() > 5000) return BadRequest("Dışa aktarım için filtreleri daraltın. Sınır 5.000 kayıt.");
        var records = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).ToListAsync();
        var csv = new StringBuilder("Kullanıcı;Zaman (Türkiye);Yazıcı;Lokasyon;Toplam baskı yüzü;Windows belge sayfası;Kopya;Baskı rengi;Durum;Kaynak;Ad soyad;Departman;Hesaplanan tutar (TL);Maliyet durumu;Windows renk talebi\r\n");
        foreach (var j in records)
            csv.AppendLine(string.Join(';', new[] { j.Account, j.LocalTime().ToString("yyyy-MM-dd HH:mm:ss"), j.Printer.Name, j.Location.Name, j.Impressions?.ToString() ?? "Doğrulanmadı", j.ReportedDocumentPages?.ToString() ?? "Bilinmiyor", j.Copies?.ToString() ?? "Bilinmiyor", j.Color.Display(), j.Status.Display(), j.Source == EventSource.Simulation ? "Simülasyon" : "Windows", j.UserDisplayName ?? "Eşleşmedi", j.DepartmentName ?? "Eşleşmedi", j.CostKurus.HasValue ? (j.CostKurus.Value / 100m).ToString("F2") : "Hesaplanmadı", PrintMerkez.Web.Services.CostCalculator.Label(j.CostState), j.RequestedColor.Display() }.Select(CsvCell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "baski-kayitlari.csv");
    }
    private static string CsvCell(string s)
    {
        // Escape spreadsheet formulas as well as CSV delimiters and quotes.
        if (s.Length > 0 && "=+-@\t\r\n".Contains(s.TrimStart().FirstOrDefault())) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
