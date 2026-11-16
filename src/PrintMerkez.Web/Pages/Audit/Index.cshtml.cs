using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Audit;
public class IndexModel(PrintDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public List<AuditEntry> Entries { get; private set; } = [];
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / 25d));
    public async Task OnGetAsync()
    {
        var query = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search)) { var s = Search.Trim(); query = query.Where(x => x.Actor.Contains(s) || x.Entity.Contains(s) || x.EntityKey.Contains(s)); }
        Total = await query.CountAsync(); PageNumber = Math.Clamp(PageNumber,1,TotalPages);
        Entries = await query.OrderByDescending(x => x.Id).Skip((PageNumber-1)*25).Take(25).ToListAsync();
    }
    public static string EntityLabel(string entity) => entity switch { "UsageAlertPolicy" => "Bildirim kuralı", "ManagerAssignment" => "Yönetici ataması", "UsageNotification" => "Bildirim", "DemoScenario" => "Örnek veri paketi", "Location" => "Lokasyon", "Printer" => "Yazıcı", "Department" => "Departman", "DirectoryUser" => "Kullanıcı", "CostTariff" => "Tarife", _ => entity };
    public static string ActionLabel(string action) => action switch { "Added" => "Eklendi", "Modified" => "Düzenlendi", "Deleted" => "Silindi", _ => action };
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));

    public static IEnumerable<FieldChange> Changes(AuditEntry entry)
    {
        var before = entry.BeforeJson is null ? new Dictionary<string,JsonElement>() : JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(entry.BeforeJson)!;
        var after = entry.AfterJson is null ? new Dictionary<string,JsonElement>() : JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(entry.AfterJson)!;
        foreach (var key in before.Keys.Union(after.Keys).Where(k => k is not "Id" and not "NormalizedAccount"))
        {
            var oldValue = before.TryGetValue(key, out var old) ? old : default;
            var newValue = after.TryGetValue(key, out var next) ? next : default;
            if (entry.Action == "Modified" && oldValue.ToString() == newValue.ToString()) continue;
            yield return new FieldChange(FieldLabel(key), Value(key,oldValue), Value(key,newValue));
        }
    }
    private static string Value(string key, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "—";
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean() ? "Evet" : "Hayır";
        if (key == "Role" && value.ValueKind == JsonValueKind.Number) return ((OrganizationRole)value.GetInt32()).Display();
        if (key == "Period" && value.ValueKind == JsonValueKind.Number) return ((UsagePeriod)value.GetInt32()).Display();
        if (key == "Source" && value.ValueKind == JsonValueKind.Number) return value.GetInt32() == (int)EventSource.Simulation ? "Simülasyon" : "Windows";
        if (key == "EffectiveFromUtc" && value.TryGetDateTime(out var utc)) return Local(utc).ToString("dd.MM.yyyy HH:mm");
        return value.ToString();
    }
    private static string FieldLabel(string key) => key switch
    {
        "Name" => "Ad", "Model" => "Model", "IpAddress" => "IP adresi", "QueueName" => "Kuyruk adı",
        "LocationId" => "Lokasyon kimliği", "IsPlaceholder" => "Geçici ad", "IsActive" => "Aktif",
        "Email" => "E-posta", "Role" => "Bildirim rolü", "Threshold" => "Aylık eşik", "ManagerUserId" => "Yönetici kimliği", "Period" => "Dönem", "Source" => "Kaynak", "Account" => "Baskı hesabı", "DisplayName" => "Ad soyad", "DepartmentId" => "Departman kimliği",
        "PrinterId" => "Yazıcı kimliği", "EffectiveFromUtc" => "Başlangıç (Türkiye saati)",
        "MonochromeKurus" => "Siyah-beyaz (kuruş/yüz)", "ColorKurus" => "Renkli (kuruş/yüz)", _ => key
    };
    public record FieldChange(string Field, string Before, string After);
}
