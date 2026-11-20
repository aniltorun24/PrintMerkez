using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Users;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty(SupportsGet=true),StringLength(160)] public string? Search { get; set; }
    [BindProperty(SupportsGet=true)] public int PageNumber { get; set; }=1;
    public List<DirectoryUser> Users { get; private set; }=[];
    public int Total { get; private set; }
    public int TotalPages=>Math.Max(1,(int)Math.Ceiling(Total/50d));
    public IActionResult OnPost()
    {
        Response.Headers.Allow="GET, HEAD";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if(id.HasValue)return RedirectToPage("/Users/Edit",new{id,Search,ListPage=PageNumber});
        if(!ModelState.IsValid)return Page();
        var query=db.DirectoryUsers.AsNoTracking().Where(u=>u.DeletedAtUtc==null).Include(u=>u.Department).Include(u=>u.Location).AsQueryable();
        if(!string.IsNullOrWhiteSpace(Search)){var search=Search.Trim();query=query.Where(u=>u.Account.Contains(search)||u.DisplayName.Contains(search));}
        Total=await query.CountAsync();PageNumber=Math.Clamp(PageNumber,1,TotalPages);
        Users=await query.OrderBy(u=>u.DisplayName).ThenBy(u=>u.Id).Skip((PageNumber-1)*50).Take(50).ToListAsync();
        return Page();
    }
}
