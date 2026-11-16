using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Services;

public sealed record ObservedDeviceJob(DeviceJobMeasurement Job, DateTime ObservedAtUtc);
public sealed record DeviceLogBatch(IReadOnlyList<ObservedDeviceJob> Jobs, bool Limited, int Rejected);
public interface IDeviceJobLogClient
{
    Task<DeviceLogBatch> ReadAsync(IReadOnlySet<string> accounts, CancellationToken ct);
}

public sealed class OlivettiDeviceJobLogClient(IConfiguration config) : IDeviceJobLogClient
{
    public async Task<DeviceLogBatch> ReadAsync(IReadOnlySet<string> accounts, CancellationToken ct)
    {
        var origin = new Uri(config["DeviceCollector:Url"] ?? throw new InvalidOperationException("Cihaz adresi gerekli."));
        var pin = config["DeviceCollector:CertificateSha256"] ?? "";
        if (origin.Scheme != "https" || !IPAddress.TryParse(origin.Host, out _) || origin.AbsolutePath != "/"
            || pin.Length != 64 || !pin.All(Uri.IsHexDigit)) throw new InvalidOperationException("Cihaz HTTPS adresi ve sertifika eşleştirmesi gerekli.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        // Self-signed device certificate: accept only the explicitly pinned pilot certificate.
        handler.ServerCertificateCustomValidationCallback = (request, cert, _, _) => request.RequestUri?.Host == origin.Host
            && cert is not null && CryptographicOperations.FixedTimeEquals(
                cert.GetCertHash(HashAlgorithmName.SHA256), Convert.FromHexString(pin));
        using var client = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 PrintMerkez/0.3");
        client.DefaultRequestHeaders.Add("Cookie", "rtl=0");
        client.DefaultRequestHeaders.ConnectionClose = true;
        async Task<string> Get(string path, string referer)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, path);
                    request.Headers.Referrer = new Uri(origin, referer);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var buffer = new MemoryStream();
                    var block = new byte[8192]; int count;
                    while ((count = await stream.ReadAsync(block, ct)) > 0)
                    {
                        if (buffer.Length + count > 262144) throw new InvalidOperationException("Cihaz yanıtı sınırı aşıldı.");
                        buffer.Write(block, 0, count);
                    }
                    return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
                }
                catch (HttpRequestException ex) when (attempt == 0 && ex.StatusCode is null) { }
            }
        }
        var jobs = new List<ObservedDeviceJob>(); var rejected = 0; var limited = false;
        var maxPages = Math.Clamp(config.GetValue("DeviceCollector:MaxPages", 3), 1, 10);
        var seen = new HashSet<string>();
        for (var page = 1; page <= maxPages; page++)
        {
            var parameters = $"arg1=3&arg2=3&arg3={page}&arg7=1";
            var text = await Get("/js/jssrc/model/job/JobSts_PrnJobLog_PrnJobs.model.htm?" + parameters,
                "/job/JobSts_PrnJobLog_PrnJobs.htm?" + parameters);
            var list = OlivettiJobLog.ParseList(text);
            foreach (var reference in list.Jobs.Where(j => accounts.Contains(DeviceCopyMatcher.AccountKey(j.Account))))
            {
                if (!seen.Add(reference.Key)) continue;
                var args = $"arg1=3&arg2={page}&arg3={reference.Key}&arg4=1";
                var started = DateTime.UtcNow;
                var detail = await Get("/js/jssrc/model/job/JobSts_PrnJobLog_PrnJob_WklyDtl.model.htm?" + args,
                    "/job/JobSts_PrnJobLog_PrnJob_WklyDtl.htm?" + args);
                var finished = DateTime.UtcNow;
                if (finished - started > TimeSpan.FromSeconds(5)) { rejected++; continue; }
                try { jobs.Add(new(OlivettiJobLog.ParseDetail(detail, reference.Key), started + (finished - started) / 2)); }
                catch (FormatException) { rejected++; }
            }
            if (list.Total <= page * 10) break;
            if (page == maxPages) limited = true;
        }
        return new(jobs, limited, rejected);
    }
}

public static class DeviceCopyMatcher
{
    public static string AccountKey(string account) => account.Trim().Split('\\').Last().ToUpperInvariant();
    public static string Key(DeviceJobMeasurement job) => $"rx:{job.Key}:{job.CompletedLocal:yyyyMMddHHmm}";
    public static IReadOnlyList<(PrintJob Print, ObservedDeviceJob Device)> Match(
        IReadOnlyList<PrintJob> pending, IReadOnlyList<ObservedDeviceJob> observed)
    {
        var pairs = (from p in pending from d in observed
            let start = d.ObservedAtUtc + (d.Job.StartedLocal - d.Job.DeviceNowLocal)
            let end = d.ObservedAtUtc + (d.Job.CompletedLocal - d.Job.DeviceNowLocal)
            where AccountKey(p.Account) == AccountKey(d.Job.Account)
                && p.ReportedDocumentPages == d.Job.DocumentPages && p.Status == PrintJobStatus.QueueCompleted
                && p.OccurredAtUtc >= start.AddMinutes(-2) && p.OccurredAtUtc <= end.AddMinutes(1)
            select (Print: p, Device: d)).ToList();
        // Do not choose a nearest match: repeated jobs or colliding shared names stay unresolved.
        return pairs.Where(pair => pairs.Count(x => x.Print.Id == pair.Print.Id) == 1
            && pairs.Count(x => Key(x.Device.Job) == Key(pair.Device.Job)) == 1).ToArray();
    }
}

public sealed record DeviceCollectorSnapshot(bool Enabled, DateTime? LastCheckUtc, int Enriched, string? Error);
public sealed class DeviceCollectorStatus
{
    private DeviceCollectorSnapshot current = new(false, null, 0, null);
    public DeviceCollectorSnapshot Current => Volatile.Read(ref current);
    public void Update(DeviceCollectorSnapshot value) => Volatile.Write(ref current, value);
}

public sealed class DeviceCopyCollector(IDeviceJobLogClient client, IServiceScopeFactory scopes,
    IConfiguration config, DeviceCollectorStatus status, ILogger<DeviceCopyCollector> logger) : BackgroundService
{
    public async Task<int> PollAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var printerId = config.GetValue<int>("DeviceCollector:PrinterId");
        var printer = await db.Printers.AsNoTracking().SingleAsync(p => p.Id == printerId, ct);
        if (!Uri.TryCreate(config["DeviceCollector:Url"], UriKind.Absolute, out var uri) || uri.Host != printer.IpAddress)
            throw new InvalidOperationException("Cihaz adresi envanterdeki yazıcıyla uyuşmuyor.");
        var context = await db.Jobs.AsNoTracking().Where(j => j.PrinterId == printerId
            && j.Source == EventSource.WindowsPrintService
            && j.ReportedDocumentPages != null && j.Status == PrintJobStatus.QueueCompleted)
            .OrderByDescending(j => j.OccurredAtUtc).Take(2000).ToListAsync(ct);
        var pending = context.Where(j => j.Impressions == null || (j.DeviceJobKey != null && j.Color == PrintColor.Unknown)).ToList();
        if (pending.Count == 0) return 0;
        var accounts = pending.Select(p => DeviceCopyMatcher.AccountKey(p.Account)).ToHashSet();
        var batch = await client.ReadAsync(accounts, ct);
        var keys = await db.Jobs.AsNoTracking().Where(j => j.PrinterId == printerId && j.DeviceJobKey != null)
            .Select(j => j.DeviceJobKey!).ToListAsync(ct);
        var available = batch.Jobs.Where(j => !keys.Contains(DeviceCopyMatcher.Key(j.Job))).ToArray();
        var newMatches = DeviceCopyMatcher.Match(context.Where(j => j.DeviceJobKey == null).ToArray(), available)
            .Where(p => p.Print.Impressions == null);
        var knownMatches = from p in pending where p.DeviceJobKey != null && p.Color == PrintColor.Unknown
            from d in batch.Jobs where DeviceCopyMatcher.Key(d.Job) == p.DeviceJobKey && d.Job.Color != PrintColor.Unknown
                && p.Copies == d.Job.Copies && p.Impressions == d.Job.Impressions
                && p.ReportedDocumentPages == d.Job.DocumentPages
                && DeviceCopyMatcher.AccountKey(p.Account) == DeviceCopyMatcher.AccountKey(d.Job.Account)
            select (Print:p,Device:d);
        var matches = newMatches.Concat(knownMatches).GroupBy(p=>p.Print.Id).Where(g=>g.Count()==1).Select(g=>g.Single()).ToArray(); var enriched = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var pair in matches)
        {
            var key = DeviceCopyMatcher.Key(pair.Device.Job);
            var job = await db.Jobs.SingleAsync(j => j.Id == pair.Print.Id, ct);
            if (job.DeviceJobKey is not null && (job.DeviceJobKey != key || job.Color != PrintColor.Unknown)) continue;
            if (job.DeviceJobKey is null && (job.Impressions is not null
                || await db.Jobs.AnyAsync(j => j.PrinterId == printerId && j.DeviceJobKey == key, ct))) continue;
            job.Copies = pair.Device.Job.Copies; job.Impressions = pair.Device.Job.Impressions;
            job.DeviceJobKey = key; job.CountVerifiedAtUtc ??= DateTime.UtcNow;
            if (job.Color == PrintColor.Unknown) job.Color = pair.Device.Job.Color;
            if (job.CostState != CostState.Calculated) await CostCalculator.CalculateAsync(job, db);
            db.AuditEntries.Add(new AuditEntry { Entity = "PrintMeasurement", EntityKey = job.Id.ToString(CultureInfo.InvariantCulture),
                Action = "Sayım ve baskı rengi cihaz iş kaydıyla eşleştirildi", Actor = "Cihaz iş kaydı toplayıcısı", OccurredAtUtc = DateTime.UtcNow,
                AfterJson = JsonSerializer.Serialize(new { job.Copies, job.Impressions, job.Color, job.DeviceJobKey, job.CountVerifiedAtUtc, job.AppliedTariffId, job.CostKurus }) });
            await db.SaveChangesAsync(ct);
            await UsageAlertService.EvaluateJobAsync(job, db);
            await db.SaveChangesAsync(ct); enriched++;
        }
        await transaction.CommitAsync(ct);
        status.Update(new(true, DateTime.UtcNow, status.Current.Enriched + enriched,
            batch.Limited || batch.Rejected > 0 ? "Cihaz geçmişinin bir bölümü okunamadı veya desteklenmiyor. Eksik toplamlar tahmin edilmedi." : null));
        return enriched;
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("DeviceCollector:Enabled", false)) return;
        status.Update(new(true, null, 0, null));
        while (!ct.IsCancellationRequested)
        {
            try { await PollAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Cihaz kopya bilgisi alınamadı: {Type}", ex.GetType().Name);
                status.Update(status.Current with { Error = "Cihaz iş kaydı okunamadı. Kopya ve toplam bilgileri tahmin edilmedi." });
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
