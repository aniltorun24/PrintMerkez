using System.Globalization;
using System.Text.RegularExpressions;

namespace PrintMerkez.Core;

public sealed record DeviceJobReference(string Key, string Account);
public sealed record DeviceJobMeasurement(string Key, string Account, DateTime StartedLocal, DateTime CompletedLocal,
    DateTime DeviceNowLocal, int DocumentPages, int Copies, int Impressions, PrintColor Color = PrintColor.Unknown);

// Adapter for the read-only job log observed on the pilot d-COPIA 255MF firmware.
// Parse only an allowlist of literal fields. Never execute JavaScript or retain document titles.
public static class OlivettiJobLog
{
    private const string Literal = "'(?:\\\\.|[^'\\\\])*'";
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static string RequiredMatch(string text, string pattern)
    {
        var match = Match(text, pattern);
        return match.Success ? match.Groups[1].Value : throw new FormatException("Cihaz iş kaydı biçimi desteklenmiyor.");
    }
    private static string StringValue(string expression)
    {
        if (!Match(expression.Trim(), "^" + Literal + "$").Success) throw new FormatException("Cihaz alanı sabit metin değil.");
        var body = expression.Trim()[1..^1];
        return Regex.Replace(body, @"\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{2}|.)", m =>
        {
            var token = m.Groups[1].Value;
            return token[0] is 'u' or 'x' ? ((char)int.Parse(token[1..], NumberStyles.HexNumber)).ToString()
                : token switch { "n" => "\n", "r" => "\r", "t" => "\t", "\\" => "\\", "'" => "'", "\"" => "\"", "/" => "/", _ => throw new FormatException("Cihaz metin kaçışı desteklenmiyor.") };
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static int Positive(string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > 1_000_000)
            throw new FormatException("Cihaz sayım değeri geçersiz.");
        return n;
    }
    private static DateTime LocalTime(string text) => DateTime.TryParseExact(text,
        ["yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
        ? DateTime.SpecifyKind(time, DateTimeKind.Unspecified) : throw new FormatException("Cihaz zamanı geçersiz.");

    public static (IReadOnlyList<DeviceJobReference> Jobs, int Total) ParseList(string script)
    {
        var total = int.Parse(RequiredMatch(script, @"_pp\.totaljobcount\s*=\s*parseInt\('(\d+)',\s*10\)"), CultureInfo.InvariantCulture);
        if (total > 10000) throw new FormatException("Cihaz iş sayısı sınırı aşıldı.");
        var rows = new List<DeviceJobReference>();
        foreach (Match block in Regex.Matches(script, @"_pp\.JobKey\[Index\]\s*=\s*'(\d+)';(.*?)(?=\r?\nIndex\s*=|_pp\.totaljobcount|$)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var user = StringValue(RequiredMatch(block.Groups[2].Value, @"_pp\.JobUsername\[Index\]\s*=\s*(" + Literal + ");"));
            rows.Add(new(block.Groups[1].Value, user));
        }
        if (total > 0 && rows.Count == 0) throw new FormatException("Cihaz iş listesi okunamadı.");
        return (rows, total);
    }

    public static DeviceJobMeasurement ParseDetail(string script, string expectedKey)
    {
        var key = StringValue(RequiredMatch(script, @"_pp\.JobNumber\s*=\s*(" + Literal + ");"));
        if (!long.TryParse(key, out var id) || !long.TryParse(expectedKey, out var expected) || id != expected)
            throw new FormatException("Cihaz iş kimliği uyuşmuyor.");
        var parts = RequiredMatch(script, @"_pp\.JobDetail\s*=\s*\[(.*?)\];").Split('\n')
            .Select(x => x.Trim().TrimEnd(',')).Where(x => x.Length > 0).ToArray();
        if (parts.Length != 10 || StringValue(parts[0]) != "2") throw new FormatException("Bu bir desteklenen yazdırma işi değil.");
        var statusValues = Regex.Matches(script, @"_pp\.tmpJobStatus\[_pp\.IndexNum\]\s*=\s*parseInt\('(\d+)',\s*10\)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ToArray();
        if (statusValues.Length == 0 || statusValues.Any(x => x != "5")) throw new FormatException("Cihaz işi başarıyla tamamlanmamış.");
        var pages = Positive(StringValue(parts[6]));
        var copies = Match(parts[7], @"^'(\d+)'\s*\+\s*'/'\s*\+\s*'(\d+)'$");
        var total = Match(parts[8], @"^'(\d+)'\s*\+\s*'/'\s*\+\s*Number\('(\d+)'\)\s*\*\s*Number\('(\d+)'\)$");
        if (!copies.Success || !total.Success) throw new FormatException("Cihaz sayım biçimi desteklenmiyor.");
        var completedCopies = Positive(copies.Groups[1].Value);
        var requestedCopies = Positive(copies.Groups[2].Value);
        var impressions = Positive(total.Groups[1].Value);
        if (completedCopies != requestedCopies || completedCopies > 100000
            || pages != Positive(total.Groups[2].Value) || requestedCopies != Positive(total.Groups[3].Value)
            || impressions != checked(pages * requestedCopies))
            throw new FormatException("Cihaz kopya ve sayfa mutabakatı başarısız.");
        var start = LocalTime(StringValue(parts[4])); var end = LocalTime(StringValue(parts[5]));
        var now = LocalTime(StringValue(RequiredMatch(script, @"_pp\.LastUpdate\s*=\s*(" + Literal + ");")));
        if (end < start || end > now.AddMinutes(1)) throw new FormatException("Cihaz zaman sırası geçersiz.");
        // The pilot firmware's sJobColorMode maps 1=full color, 2=gray, 3=black/white.
        // Auto/mixed modes cannot safely select a single rate for the entire job.
        var colorMatch = Match(parts[9], @"^parseInt\('(\d+)',\s*10\)$");
        var overrides = Regex.Matches(script, @"_pp\.JobDetail\[9\]\s*=\s*(\d+)\s*;",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var code = overrides.Count == 1 ? overrides[0].Groups[1].Value
            : overrides.Count == 0 && colorMatch.Success ? colorMatch.Groups[1].Value : "";
        var color = code switch { "1" => PrintColor.Color, "2" or "3" => PrintColor.Monochrome, _ => PrintColor.Unknown };
        return new(key, StringValue(parts[2]), start, end, now, pages, completedCopies, impressions, color);
    }
}
