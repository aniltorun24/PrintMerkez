using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Locations;
public class IndexModel(PrintDbContext db) : PageModel
{
    public List<Location> Locations { get; private set; } = [];
    public Dictionary<int,int> Counts { get; private set; } = [];
    public async Task OnGetAsync()
    {
        Locations = await db.Locations.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        Counts = await db.Printers.GroupBy(x => x.LocationId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
    }
}