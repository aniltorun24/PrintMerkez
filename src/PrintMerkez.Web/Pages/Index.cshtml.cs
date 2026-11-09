using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages;
public class IndexModel(PrintDbContext db) : PageModel
{
    public int Locations { get; private set; }
    public int Printers { get; private set; }
    public int Events { get; private set; }
    public int UnknownImpressions { get; private set; }
    public long Impressions { get; private set; }
    public int Simulations { get; private set; }
    public List<PrintJob> RecentJobs { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Locations = await db.Locations.CountAsync();
        Printers = await db.Printers.CountAsync();
        Events = await db.Jobs.CountAsync();
        Impressions = await db.Jobs.SumAsync(j => (long?)j.Impressions) ?? 0;
        UnknownImpressions = await db.Jobs.CountAsync(j => j.Impressions == null);
        Simulations = await db.Jobs.CountAsync(j => j.Source == EventSource.Simulation);
        RecentJobs = await db.Jobs.Include(j => j.Printer).Include(j => j.Location).OrderByDescending(j => j.OccurredAtUtc).ThenByDescending(j => j.Id).Take(6).AsNoTracking().ToListAsync();
    }
}