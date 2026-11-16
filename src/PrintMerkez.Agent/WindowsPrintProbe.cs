using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Text.Json;
using PrintMerkez.Core;

namespace PrintMerkez.Agent;

// Read-only, bounded pilot. This is deliberately not a persistent Windows Service collector.
[SupportedOSPlatform("windows")]
public static class WindowsPrintProbe
{
    public static int Run(IConfiguration config)
    {
        var server = config["Windows:Server"]?.Trim();
        var queue = config["Windows:Queue"]?.Trim();
        var maxEvents = config.GetValue("Windows:MaxEvents", 50);
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(queue)
            || maxEvents < 1 || maxEvents > 1000)
            throw new InvalidOperationException("Sunucu, tam kuyruk adı ve 1-1000 kayıt sınırı gerekli.");
        using var session = new EventLogSession(server);
        using var log = new EventLogConfiguration(WindowsPrintEventParser.Channel, session);
        if (!log.IsEnabled)
            throw new InvalidOperationException("PrintService/Operational günlüğü etkin değil.");
        var query = new EventLogQuery(WindowsPrintEventParser.Channel, PathType.LogName,
            "*[System[Provider[@Name='Microsoft-Windows-PrintService'] and (EventID=307)]]")
        {
            Session = session, ReverseDirection = true, TolerateQueryErrors = false
        };
        using var reader = new EventLogReader(query);
        var rows = new List<WindowsPrintObservation>();
        var rejected = 0;
        var otherQueues = 0;
        var scanned = 0;
        while (scanned < maxEvents)
        {
            using var entry = reader.ReadEvent(TimeSpan.FromSeconds(10));
            if (entry is null) break;
            scanned++;
            WindowsPrintObservation observation;
            try { observation = WindowsPrintEventParser.Parse(entry.ToXml()); }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
            {
                // Never print raw XML or exception contents: those can contain document titles.
                rejected++;
                continue;
            }
            if (!string.Equals(observation.QueueName, queue, StringComparison.OrdinalIgnoreCase))
            { otherQueues++; continue; }
            rows.Add(observation);
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Mode = "ReadOnlyProbe", Server = server, Queue = queue, LogEnabled = log.IsEnabled,
            Scanned = scanned, MaxEvents = maxEvents, OtherQueues = otherQueues, Rejected = rejected,
            MatchingEvents = rows.Count, Events = rows,
            Note = "Yalnızca okundu. API/veritabanı değişmedi. Sayfalar sürücünün bildirdiği değerdir; kopya, renk ve fiziksel çıktı doğrulanmadı."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return rejected == 0 ? 0 : 2;
    }
}
