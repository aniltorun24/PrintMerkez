using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Services;
public static class CostCalculator
{
    public static async Task EnrichAsync(PrintJob job, PrintDbContext db)
    {
        var normalized = DirectoryUser.Normalize(job.Account);
        job.NormalizedAccount = normalized;
        var user = await db.DirectoryUsers.AsNoTracking().Include(u => u.Department)
            .SingleOrDefaultAsync(u => u.NormalizedAccount == normalized);
        job.DirectoryUserId = user?.Id;
        job.UserDisplayName = user?.DisplayName;
        job.DepartmentName = user?.Department?.Name;
        await CalculateAsync(job, db);
    }
    public static async Task CalculateAsync(PrintJob job, PrintDbContext db)
    {
        if (job.Status != PrintJobStatus.QueueCompleted) { job.CostState = CostState.NotChargeable; return; }
        if (job.Impressions is null || job.Color == PrintColor.Unknown) { job.CostState = CostState.MissingMetadata; return; }
        var tariff = await db.Tariffs.AsNoTracking().Where(t => t.PrinterId == job.PrinterId && t.EffectiveFromUtc <= job.OccurredAtUtc)
            .OrderByDescending(t => t.EffectiveFromUtc).FirstOrDefaultAsync();
        if (tariff is null) { job.CostState = CostState.NoTariff; return; }
        var unit = job.Color == PrintColor.Color ? tariff.ColorKurus : tariff.MonochromeKurus;
        job.AppliedUnitKurus = unit; job.AppliedTariffId = tariff.Id;
        job.CostKurus = checked((long)job.Impressions.Value * unit);
        job.CostState = CostState.Calculated;
    }
    public static string Label(CostState state) => state switch
    {
        CostState.Calculated => "Hesaplandı", CostState.MissingMetadata => "Adet veya renk eksik",
        CostState.NoTariff => "Geçerli tarife yok", CostState.NotChargeable => "Tamamlanmamış / ücretlendirilmez", _ => "Henüz hesaplanmadı"
    };
}
