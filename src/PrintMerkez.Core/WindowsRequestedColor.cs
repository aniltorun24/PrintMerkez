using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PrintMerkez.Core;

public static class WindowsRequestedColor
{
    // 805 describes Windows/driver settings, not proof of the color physically printed.
    public static PrintColor Parse(string xml, WindowsPrintObservation completed)
    {
        if (completed.JobId is null || completed.JobSize is null) return PrintColor.Unknown;
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        var root = XDocument.Load(reader).Root;
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var system = root?.Element(ns + "System");
        if (root?.Name != ns + "Event" || system is null
            || (string?)system.Element(ns + "Provider")?.Attribute("Name") != WindowsPrintEventParser.Provider
            || (string?)system.Element(ns + "EventID") != "805" || (string?)system.Element(ns + "Version") != "0"
            || (string?)system.Element(ns + "Channel") != WindowsPrintEventParser.Channel
            || !string.Equals((string?)system.Element(ns + "Computer"), completed.Server, StringComparison.OrdinalIgnoreCase))
            return PrintColor.Unknown;
        if (!DateTimeOffset.TryParse((string?)system.Element(ns + "TimeCreated")?.Attribute("SystemTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            || time > completed.OccurredAt || time < completed.OccurredAt.AddSeconds(-30)) return PrintColor.Unknown;
        string? Field(string name)
        {
            var values = (root.Element(ns + "UserData")?.Elements().Elements() ?? [])
                .Where(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value)
                .Concat((root.Element(ns + "EventData")?.Elements(ns + "Data") ?? [])
                    .Where(e => string.Equals((string?)e.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value)).ToArray();
            return values.Length == 1 ? values[0] : null;
        }
        if (!long.TryParse(Field("JobId"), out var job) || job != completed.JobId
            || !long.TryParse(Field("GdiJobSize"), out var size) || size != completed.JobSize) return PrintColor.Unknown;
        return Field("Color") switch { "1" => PrintColor.Monochrome, "2" => PrintColor.Color, _ => PrintColor.Unknown };
    }
}
