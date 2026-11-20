using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Services;

public sealed record CollectedWindowsEvent(string Xml, string Bookmark, PrintColor RequestedColor = PrintColor.Unknown);
public sealed class WindowsCursorLostException : Exception;
public sealed class WindowsCollectorConfigurationException(string message) : InvalidOperationException(message);
public interface IWindowsPrintReader
{
    IReadOnlyList<CollectedWindowsEvent> Read(string server, string? bookmark, string? expectedEventId, int limit);
}

public sealed class WindowsPrintReader : IWindowsPrintReader
{
    public static bool IsLocalServer(string server, string machineName, string domainName)
    {
        var name = server.Trim().TrimEnd('.');
        return string.Equals(name, machineName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(domainName) && string.Equals(name,
                machineName + "." + domainName.Trim('.'), StringComparison.OrdinalIgnoreCase));
    }
    public IReadOnlyList<CollectedWindowsEvent> Read(string server, string? bookmark, string? expectedEventId, int limit)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var local = IsLocalServer(server, Environment.MachineName, IPGlobalProperties.GetIPGlobalProperties().DomainName);
        using var session = local ? new EventLogSession() : new EventLogSession(server);
        using var channel = new EventLogConfiguration(WindowsPrintEventParser.Channel, session);
        if (!channel.IsEnabled) throw new InvalidOperationException("Baskı olay günlüğü kapalı.");
        if (bookmark is not null && expectedEventId is not null)
        {
            // A cleared log can reuse a record number. Verify the event identity before seeking.
            var recordText = XDocument.Parse(bookmark).Descendants().Attributes("RecordId").SingleOrDefault()?.Value;
            if (!long.TryParse(recordText, out var recordId) || recordId < 1) throw new WindowsCursorLostException();
            var anchorQuery = new EventLogQuery(WindowsPrintEventParser.Channel, PathType.LogName,
                $"*[System[EventRecordID={recordId}]]") { Session = session };
            using var anchorReader = new EventLogReader(anchorQuery);
            using var anchor = anchorReader.ReadEvent(TimeSpan.FromSeconds(5));
            if (anchor is null || WindowsPrintEventParser.Parse(anchor.ToXml()).EventId != expectedEventId)
                throw new WindowsCursorLostException();
        }
        var query = new EventLogQuery(WindowsPrintEventParser.Channel, PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-PrintService'] and (EventID=307)]]")
        { Session = session, ReverseDirection = false, TolerateQueryErrors = false };
        using var reader = new EventLogReader(query, bookmark is null ? null : new EventBookmark(bookmark));
        var rows = new List<CollectedWindowsEvent>();
        for (var i = 0; i < limit; i++)
        {
            using var record = reader.ReadEvent(TimeSpan.FromSeconds(5));
            if (record is null) break;
            var xml = record.ToXml();
            var observation = WindowsPrintEventParser.Parse(xml);
            var requested = ReadRequestedColor(session, observation);
            rows.Add(new(xml, record.Bookmark.BookmarkXml, requested));
        }
        return rows;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static PrintColor ReadRequestedColor(EventLogSession session, WindowsPrintObservation completed)
    {
        if (completed.JobId is null || completed.JobSize is null) return PrintColor.Unknown;
        var from = completed.OccurredAt.AddSeconds(-30).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        var to = completed.OccurredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        var query = new EventLogQuery(WindowsPrintEventParser.Channel, PathType.LogName,
            $"*[System[EventID=805 and TimeCreated[@SystemTime >= '{from}' and @SystemTime <= '{to}']]]") { Session = session };
        using var reader = new EventLogReader(query);
        var matches = new List<PrintColor>();
        for (var n = 0; n < 100; n++)
        {
            using var row = reader.ReadEvent(TimeSpan.FromSeconds(5));
            if (row is null) break;
            var color = WindowsRequestedColor.Parse(row.ToXml(), completed);
            if (color != PrintColor.Unknown) matches.Add(color);
            if (n == 99) return PrintColor.Unknown;
        }
        return matches.Count == 1 ? matches[0] : PrintColor.Unknown;
    }
}

public sealed record CollectorSnapshot(bool Enabled, DateTime? LastCheckUtc, DateTime? LastEventUtc,
    string? Error, int Imported, string? Warning);
public sealed class WindowsCollectorStatus
{
    private CollectorSnapshot current = new(false, null, null, null, 0, null);
    public CollectorSnapshot Current => Volatile.Read(ref current);
    public void Update(CollectorSnapshot value) => Volatile.Write(ref current, value);
}

public sealed record WindowsCollectorCursor(string? Bookmark, DateTime? LastEventUtc, int Imported, string? Warning,
    string? AnchorEventId = null);

// Persist only AFTER database acceptance. A crash before cursor save replays safely via Server/EventId.
public sealed class WindowsCollector(IWindowsPrintReader reader, WindowsCollectorStatus status,
    IServiceScopeFactory scopes, IConfiguration config, IWebHostEnvironment environment,
    ILogger<WindowsCollector> logger) : BackgroundService
{
    public string CursorPath
    {
        get => GetCursorPath(config, environment);
    }
    public static string GetCursorPath(IConfiguration config, IHostEnvironment environment)
    {
            var identity = $"{config.GetConnectionString("PrintDb")}|{config["WindowsCollector:Server"]?.ToUpperInvariant()}|{config["WindowsCollector:Queue"]?.ToUpperInvariant()}|{config["WindowsCollector:PrinterId"]}";
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
            var directory = config["WindowsCollector:CursorDirectory"] ?? Path.Combine(environment.ContentRootPath, "App_Data");
            return Path.Combine(directory, $"windows-cursor-{hash}.json");
    }

    public async Task PollAsync(CancellationToken ct)
    {
        var server = config["WindowsCollector:Server"]?.Trim();
        var queue = config["WindowsCollector:Queue"]?.Trim();
        var printerId = config.GetValue<int>("WindowsCollector:PrinterId");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(queue) || printerId < 1)
            throw new InvalidOperationException("Sunucu, kuyruk ve envanter eşleştirmesi gerekli.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrintDbContext>();
        var printer = await db.Printers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == printerId, ct);
        if (printer is null || !string.Equals(printer.QueueName, queue, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Yazıcı kuyruk eşleştirmesi uyuşmuyor.");
        Directory.CreateDirectory(Path.GetDirectoryName(CursorPath)!);
        // Serialize polls even if the same local database is accidentally opened in two app processes.
        using var cursorLock = new FileStream(CursorPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var cursor = File.Exists(CursorPath)
            ? JsonSerializer.Deserialize<WindowsCollectorCursor>(await File.ReadAllTextAsync(CursorPath, ct))
                ?? throw new InvalidOperationException("Toplayıcı devam kaydı okunamadı.")
            : new WindowsCollectorCursor(null, null, 0, null);
        IReadOnlyList<CollectedWindowsEvent> rows;
        try { rows = reader.Read(server, cursor.Bookmark, cursor.AnchorEventId, 200); }
        catch (Exception ex) when (cursor.Bookmark is not null && ex is EventLogException or WindowsCursorLostException)
        {
            // Retained events can be replayed; already overwritten events cannot be recovered.
            cursor = cursor with { Bookmark = null, AnchorEventId = null, Warning = "Olay günlüğündeki devam noktası kayboldu. Saklanan kayıtlar yeniden tarandı; arada kayıp olabilir." };
            rows = reader.Read(server, null, null, 200);
        }
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            // Unexpected/malformed records stop progress visibly; they are never silently discarded.
            var observation = WindowsPrintEventParser.Parse(row.Xml);
            if (string.Equals(observation.QueueName, queue, StringComparison.OrdinalIgnoreCase))
            {
                var allowedServers = config.GetSection("WindowsCollector:AllowedEventServers").GetChildren()
                    .Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Append(server);
                if (!allowedServers.Any(name => string.Equals(name?.Trim().TrimEnd('.'), observation.Server.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)))
                    throw new WindowsCollectorConfigurationException($"Kaynak sunucu adı eşleşmiyor: {observation.Server}. Sunucunun adı değiştiyse bağlantı ve izin verilen kaynak adları güncellenmeli.");
                var request = new PrintEventRequest
                {
                    EventId = observation.EventId, Server = observation.Server, PrinterId = printerId,
                    Account = observation.Account, OccurredAt = observation.OccurredAt,
                    ReportedDocumentPages = observation.ReportedPages,
                    Impressions = null, Copies = null, Color = PrintColor.Unknown,
                    RequestedColor = row.RequestedColor,
                    Status = PrintJobStatus.QueueCompleted, Source = EventSource.WindowsPrintService
                };
                var result = await EventIngestion.AcceptAsync(request, db);
                if (result is not IStatusCodeHttpResult { StatusCode: 200 or 201 })
                    throw new InvalidOperationException("Baskı kaydı veritabanına kabul edilmedi.");
                // A replay can supplement this metadata without replacing the original job or accounting values.
                if (row.RequestedColor != PrintColor.Unknown)
                {
                    var stored = await db.Jobs.SingleAsync(j => j.Server == request.Server && j.EventId == request.EventId, ct);
                    if (stored.RequestedColor == PrintColor.Unknown)
                    {
                        stored.RequestedColor = row.RequestedColor;
                        db.AuditEntries.Add(new AuditEntry { Entity = "PrintRequestedColor", EntityKey = stored.Id.ToString(),
                            Actor = "Windows olay toplayıcısı", Action = "Windows renk talebi 805 kaydından tamamlandı", OccurredAtUtc = DateTime.UtcNow });
                        await db.SaveChangesAsync(ct);
                    }
                }
                var added = result is IStatusCodeHttpResult { StatusCode: 201 };
                cursor = cursor with { LastEventUtc = observation.OccurredAt.UtcDateTime, Imported = cursor.Imported + (added ? 1 : 0) };
            }
            cursor = cursor with { Bookmark = row.Bookmark, AnchorEventId = observation.EventId };
            // Persist each accepted/skipped-other-queue event, avoiding a large replay after a fault.
            Directory.CreateDirectory(Path.GetDirectoryName(CursorPath)!);
            var temporary = CursorPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(cursor), ct);
            File.Move(temporary, CursorPath, overwrite: true);
        }
        status.Update(new(true, DateTime.UtcNow, cursor.LastEventUtc, null, cursor.Imported, cursor.Warning));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("WindowsCollector:Enabled", false)) return;
        status.Update(new(true, null, null, null, 0, null));
        var seconds = Math.Clamp(config.GetValue("WindowsCollector:PollSeconds", 10), 5, 300);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Run(() => PollAsync(stoppingToken), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // No raw XML or exception text; a provider error may disclose document titles.
                logger.LogWarning("Windows baskı aktarımı bekliyor. Hata türü: {ErrorType}", ex.GetType().Name);
                status.Update(status.Current with { Error = ex is WindowsCollectorConfigurationException ? ex.Message
                    : "Aktarım durdu. Sunucu erişimi, okuma yetkisi, kuyruk eşleştirmesi ve olay biçimini kontrol edin." });
            }
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
