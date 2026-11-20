using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Web.Pages.Users;

public enum UsageMetric { Impressions, Jobs }

public class DetailsModel(PrintDbContext db, UserDailyUsage usage) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true), EnumDataType(typeof(EventSource))]
    public EventSource Source { get; set; } = EventSource.WindowsPrintService;
    [BindProperty(SupportsGet = true), EnumDataType(typeof(UsageMetric))]
    public UsageMetric Metric { get; set; } = UsageMetric.Impressions;
    public DirectoryUser Person { get; private set; } = null!;
    public IReadOnlyList<DailyUsage> Days { get; private set; } = [];
    public long Jobs => Days.Sum(d => d.Jobs);
    public long UnknownJobs => Days.Sum(d => d.UnknownJobs);
    public long? Impressions => Jobs > 0 && UnknownJobs == Jobs ? null : Days.Sum(d => d.Impressions);
    public long? WindowsPages => Jobs > 0 && Days.Sum(d => d.KnownWindowsJobs) == 0 ? null : Days.Sum(d => d.WindowsPages);
    public string MetricLabel => Metric == UsageMetric.Jobs ? "Belge / baskı işi" : "Doğrulanmış baskı yüzü";
    public long? Value(DailyUsage day) => Metric == UsageMetric.Jobs ? day.Jobs : day.ConfirmedTotal;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.DirectoryUsers.AsNoTracking().Include(u => u.Department).Include(u => u.Location)
            .SingleOrDefaultAsync(u => u.Id == id && u.DeletedAtUtc == null, HttpContext.RequestAborted);
        if (person is null) return NotFound();
        Person = person;
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, UserDailyUsage.TurkeyZone).Date;
        To ??= today;
        From ??= (To.Value.Year is >= 2000 and <= 2100 ? To.Value.Date : today).AddDays(-29);
        if (From.Value.Year < 2000 || From.Value.Year > 2100 || To.Value.Year < 2000 || To.Value.Year > 2100)
            ModelState.AddModelError("", "Tarih 2000–2100 aralığında olmalı.");
        if (From.Value.Date > To.Value.Date) ModelState.AddModelError("", "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        if ((To.Value.Date - From.Value.Date).Days >= 366) ModelState.AddModelError("", "En fazla 366 gün seçebilirsiniz.");
        if (ModelState.IsValid)
            Days = await usage.ReadAsync(Person, From.Value, To.Value, Source, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnGetLiveAsync(int id, [FromServices] LiveUpdates live)
    {
        var unchanged = LiveUpdates.NotModified(this, live);
        if (unchanged is not null) return unchanged;
        var result = await OnGetAsync(id);
        if (result is not PageResult) return result;
        if (!ModelState.IsValid) return BadRequest("Geçersiz filtre.");
        return Partial("_UserDailyUsage", this);
    }
}
