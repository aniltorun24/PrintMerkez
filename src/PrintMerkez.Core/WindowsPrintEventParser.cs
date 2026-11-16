using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PrintMerkez.Core;

// A probe result, not an accounting record. Driver page/copy semantics need a real-device pilot.
public sealed record WindowsPrintObservation(
    string EventId, string Server, long RecordId, DateTimeOffset OccurredAt,
    string Account, string ClientComputer, string QueueName, string PortName, int? ReportedPages,
    long? JobId = null, long? JobSize = null);

public static class WindowsPrintEventParser
{
    public const string Channel = "Microsoft-Windows-PrintService/Operational";
    public const string Provider = "Microsoft-Windows-PrintService";
    private static readonly XNamespace Events = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static WindowsPrintObservation Parse(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144
        });
        var root = XDocument.Load(reader).Root;
        var system = root?.Element(Events + "System");
        if (root?.Name != Events + "Event" || system is null
            || (string?)system.Element(Events + "Provider")?.Attribute("Name") != Provider
            || (string?)system.Element(Events + "Channel") != Channel
            || (string?)system.Element(Events + "EventID") != "307"
            || (string?)system.Element(Events + "Version") != "0")
            throw new FormatException("Desteklenmeyen PrintService olayı; yalnızca 307 sürüm 0 okunur.");

        var server = Required((string?)system.Element(Events + "Computer"), 80).ToUpperInvariant();
        if (!long.TryParse((string?)system.Element(Events + "EventRecordID"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var recordId) || recordId <= 0)
            throw new FormatException("Olay kayıt kimliği geçersiz.");
        var timeText = (string?)system.Element(Events + "TimeCreated")?.Attribute("SystemTime");
        if (timeText is null || !timeText.EndsWith('Z')
            || !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var occurredAt))
            throw new FormatException("UTC olay zamanı geçersiz.");

        // Windows renders this event as UserData/DocumentPrinted; EventData is also supported.
        // Deliberately select only needed fields: param2 is a document title and is never returned.
        string? Field(string name)
        {
            var structured = root.Element(Events + "UserData")?.Elements()
                .Where(x => x.Name.LocalName == "DocumentPrinted")
                .Elements().Where(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).ToArray() ?? [];
            var generic = root.Element(Events + "EventData")?.Elements(Events + "Data")
                .Where(x => string.Equals((string?)x.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).ToArray() ?? [];
            var values = structured.Concat(generic).ToArray();
            if (values.Length > 1) throw new FormatException("Tekrarlanan olay alanı.");
            return values.SingleOrDefault();
        }
        var account = Required(Field("param3"), 160);
        var client = Required(Field("param4"), 256);
        var queue = Required(Field("param5"), 256);
        var port = Required(Field("param6"), 256);
        var rawPages = Field("param8");
        int? pages = null;
        if (!string.IsNullOrWhiteSpace(rawPages))
        {
            if (!int.TryParse(rawPages, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 0 || parsed > 1_000_000)
                throw new FormatException("Raporlanan sayfa sayısı geçersiz.");
            // Zero may be reported for RAW/driver-specific jobs; never count it as a known zero.
            if (parsed > 0) pages = parsed;
        }

        // Record IDs can restart after a channel clear. The timestamp separates those generations.
        // No document title, account, or other personal data participates in the stable identity.
        var identity = $"{server}|{Channel}|307|0|{recordId}|{occurredAt.UtcTicks}";
        var eventId = "win307-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        long? Number(string field) => long.TryParse(Field(field), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
        return new(eventId, server, recordId, occurredAt, account, client, queue, port, pages, Number("param1"), Number("param7"));
    }

    private static string Required(string? value, int maxLength)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
            throw new FormatException("Zorunlu olay alanı eksik veya çok uzun.");
        return value;
    }
}
