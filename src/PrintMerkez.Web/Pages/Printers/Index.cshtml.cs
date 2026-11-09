using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Printers;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? LocationId { get; set; }
    public List<Printer> Printers { get; private set; } = [];
    public List<Location> Locations { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Locations = await db.Locations.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var query = db.Printers.AsNoTracking().Include(x => x.Location).AsQueryable();
        if (LocationId.HasValue) query = query.Where(x => x.LocationId == LocationId);
        if (!string.IsNullOrWhiteSpace(Search)) { var s = Search.Trim(); query = query.Where(x => x.Name.Contains(s) || x.Model.Contains(s) || (x.IpAddress != null && x.IpAddress.Contains(s))); }
        Printers = await query.OrderBy(x => x.Name).Take(500).ToListAsync();
    }
}