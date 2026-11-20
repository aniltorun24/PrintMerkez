using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Services;

public sealed record DailyUsage(DateTime Day, long Jobs, long KnownJobs, long Impressions,
    long WindowsPages, long KnownWindowsJobs)
{
    public long UnknownJobs => Jobs - KnownJobs;
    public long? ConfirmedTotal => Jobs > 0 && KnownJobs == 0 ? null : Impressions;
}

public sealed class UserDailyUsage(PrintDbContext db)
{
    public static TimeZoneInfo TurkeyZone => TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task<IReadOnlyList<DailyUsage>> ReadAsync(DirectoryUser user, DateTime from, DateTime to,
        EventSource source, CancellationToken cancellationToken)
    {
        if (from.Year < 2000 || from.Year > 2100 || to.Year < 2000 || to.Year > 2100 || from.Date > to.Date || (to.Date - from.Date).Days >= 366)
            throw new ArgumentOutOfRangeException(nameof(from), "En fazla 366 günlük geçerli bir aralık seçin.");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified), TurkeyZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Unspecified), TurkeyZone);
        // Keep historical ID matches; also report older unassigned jobs with the exact normalized account.
        // Aggregate in SQL, not by downloading every print event. Hour buckets preserve local midnight boundaries.
        var aliases = DirectoryAccounts.Aliases(user);
        var hours = await db.Jobs.AsNoTracking()
            .Where(j => (j.DirectoryUserId == user.Id || (j.DirectoryUserId == null && aliases.Contains(j.NormalizedAccount)))
                && j.Source == source && j.Status == PrintJobStatus.QueueCompleted
                && j.OccurredAtUtc >= startUtc && j.OccurredAtUtc < endUtc)
            .GroupBy(j => new { Day = j.OccurredAtUtc.Date, Hour = j.OccurredAtUtc.Hour })
            .Select(g => new
            {
                g.Key.Day, g.Key.Hour,
                Jobs = g.LongCount(), KnownJobs = g.LongCount(j => j.Impressions != null),
                Impressions = g.Sum(j => (long?)j.Impressions) ?? 0,
                WindowsPages = g.Sum(j => (long?)j.ReportedDocumentPages) ?? 0,
                KnownWindowsJobs = g.LongCount(j => j.ReportedDocumentPages != null)
            }).ToListAsync(cancellationToken);
        var days = hours.GroupBy(h => TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(h.Day.AddHours(h.Hour), DateTimeKind.Utc), TurkeyZone).Date)
            .ToDictionary(g => g.Key, g => new DailyUsage(g.Key, g.Sum(h => h.Jobs), g.Sum(h => h.KnownJobs),
                g.Sum(h => h.Impressions), g.Sum(h => h.WindowsPages), g.Sum(h => h.KnownWindowsJobs)));
        return Enumerable.Range(0, (to.Date - from.Date).Days + 1).Select(offset =>
        {
            var date = from.Date.AddDays(offset);
            return days.GetValueOrDefault(date) ?? new DailyUsage(date, 0, 0, 0, 0, 0);
        }).ToArray();
    }
}
