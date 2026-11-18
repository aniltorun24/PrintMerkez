using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages;
public class IndexModel(PrintDbContext db) : PageModel
{
    public async Task<IActionResult> OnGetLiveAsync([FromServices] PrintMerkez.Web.Services.LiveUpdates live)
    {
        var unchanged = PrintMerkez.Web.Services.LiveUpdates.NotModified(this, live);
        if (unchanged is not null) return unchanged;
        await OnGetAsync();
        if (!ModelState.IsValid) return BadRequest("Geçersiz filtre.");
        return Partial("_DashboardLive", this);
    }
    [BindProperty(SupportsGet = true), EnumDataType(typeof(EventSource))] public EventSource Source { get; set; } = EventSource.WindowsPrintService;
    public int Locations { get; private set; }
    public int Printers { get; private set; }
    public int Events { get; private set; }
    public int UnknownImpressions { get; private set; }
    public long Impressions { get; private set; }
    public long? CostKurus { get; private set; }
    public int PricedJobs { get; private set; }
    public int MissingCosts { get; private set; }
    public int UnmappedUsers { get; private set; }
    public List<PrintJob> RecentJobs { get; private set; } = [];
    public List<LocationTotal> ByLocation { get; private set; } = [];
    public async Task OnGetAsync()
    {
        if (!ModelState.IsValid) return;
        Locations = await db.Locations.CountAsync(); Printers = await db.Printers.CountAsync();
        var jobs = db.Jobs.AsNoTracking().Where(j => j.Source == Source);
        Events = await jobs.CountAsync();
        Impressions = await jobs.SumAsync(j => (long?)j.Impressions) ?? 0;
        UnknownImpressions = await jobs.CountAsync(j => j.Impressions == null);
        PricedJobs = await jobs.CountAsync(j => j.CostState == CostState.Calculated);
        CostKurus = PricedJobs == 0 ? null : await jobs.SumAsync(j => j.CostKurus);
        MissingCosts = await jobs.CountAsync(j => j.CostState != CostState.Calculated && j.CostState != CostState.NotChargeable);
        UnmappedUsers = await jobs.CountAsync(j => j.UserDisplayName == null);
        RecentJobs = await jobs.Include(j => j.Printer).Include(j => j.Location).OrderByDescending(j => j.OccurredAtUtc).ThenByDescending(j => j.Id).Take(6).ToListAsync();
        ByLocation = await jobs.GroupBy(j => new { j.LocationId, j.Location.Name })
            .Select(g => new LocationTotal { Name = g.Key.Name, Jobs = g.Count(), UnknownImpressions = g.Count(j => j.Impressions == null), Impressions = g.Sum(j => (long?)j.Impressions) ?? 0, CostKurus = g.Count(j => j.CostState == CostState.Calculated) == 0 ? null : g.Sum(j => j.CostKurus), MissingCosts = g.Count(j => j.CostState != CostState.Calculated && j.CostState != CostState.NotChargeable) })
            .OrderByDescending(g => g.Jobs).Take(20).ToListAsync();
    }
    public sealed class LocationTotal
    {
        public string Name { get; set; } = "";
        public int Jobs { get; set; }
        public int UnknownImpressions { get; set; }
        public long Impressions { get; set; }
        public long? CostKurus { get; set; }
        public int MissingCosts { get; set; }
    }
}
