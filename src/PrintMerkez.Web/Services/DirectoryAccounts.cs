using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Services;

public static class DirectoryAccounts
{
    public static string[] Aliases(DirectoryUser user) => new[]
    {
        user.NormalizedAccount,
        user.AdSamAccountName,
        user.AdDomain is not null && user.AdSamAccountName is not null ? user.AdDomain + "\\" + user.AdSamAccountName : null
    }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => DirectoryUser.Normalize(x!)).Distinct().ToArray();

    public static async Task<DirectoryUser?> FindAsync(string account, PrintDbContext db)
    {
        var normalized = DirectoryUser.Normalize(account);
        var exact = await db.DirectoryUsers.Where(u => u.DeletedAtUtc == null).Include(u => u.Department).Include(u => u.Location)
            .SingleOrDefaultAsync(u => u.NormalizedAccount == normalized);
        if (exact is not null) return exact;
        var split = normalized.Split('\\');
        var matches = db.DirectoryUsers.Where(u => u.DeletedAtUtc == null).Include(u => u.Department).Include(u => u.Location).AsQueryable();
        if (split.Length == 2) matches = matches.Where(u => u.AdDomain == split[0] && u.AdSamAccountName == split[1]);
        else if (split.Length == 1) matches = matches.Where(u => u.AdSamAccountName == normalized);
        else return null;
        var candidates = await matches.Take(2).ToListAsync();
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
