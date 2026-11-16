using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
namespace PrintMerkez.Core;

// Organizational roles used for notification routing, not application access permissions.
public enum OrganizationRole { Employee, Manager }
public enum UsagePeriod { Monthly, Daily, PerJob }
public enum NotificationState { Pending, Sending, Previewed, Sent, Blocked, Retry, Failed, Uncertain, Cancelled }

public sealed class ManagerAssignment
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
    public int ManagerUserId { get; set; }
    public DirectoryUser ManagerUser { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public sealed class UsageAlertPolicy
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Range(1, 1_000_000_000)] public int Threshold { get; set; }
    public UsagePeriod Period { get; set; } = UsagePeriod.Monthly;
    public EventSource Source { get; set; } = EventSource.WindowsPrintService;
    public bool IsActive { get; set; } = true;
}

public sealed class UsageNotification
{
    public long Id { get; set; }
    public int PolicyId { get; set; }
    public UsageAlertPolicy Policy { get; set; } = null!;
    public int UserId { get; set; }
    public DirectoryUser User { get; set; } = null!;
    public string PeriodKey { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public EventSource Source { get; set; }
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    public long TotalImpressions { get; set; }
    public int Threshold { get; set; }
    public string Account { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string? RecipientEmail { get; set; }
    public int? ManagerUserId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N") + "@printmerkez.local";
    public NotificationState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ClaimToken { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public static class NotificationLabels
{
    public static string Display(this UsagePeriod p) => p switch { UsagePeriod.Daily => "Günlük toplam", UsagePeriod.PerJob => "Tek baskı işi", _ => "Aylık toplam" };
    public static string Display(this OrganizationRole r) => r == OrganizationRole.Manager ? "Yönetici" : "Çalışan";
    public static string Display(this NotificationState s) => s switch
    {
        NotificationState.Pending => "Kuyrukta", NotificationState.Sending => "Gönderiliyor", NotificationState.Previewed => "Önizleme / mail gönderilmedi",
        NotificationState.Sent => "SMTP sunucusu kabul etti", NotificationState.Blocked => "Eşleştirme bekliyor", NotificationState.Retry => "Tekrar denenecek",
        NotificationState.Failed => "Gönderilemedi", NotificationState.Uncertain => "Teslim durumu belirsiz", _ => "İptal edildi"
    };
    public static bool IsEmail(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Contains('\r') && !value.Contains('\n')
        && MailAddress.TryCreate(value, out var address) && string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
}
