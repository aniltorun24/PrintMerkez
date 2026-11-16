using System.ComponentModel.DataAnnotations;
namespace PrintMerkez.Core;

public sealed class Department
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

// Directory metadata only. This is not a login account or authentication provider.
public sealed class DirectoryUser
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Account { get; set; } = "";
    [Required, StringLength(160)] public string NormalizedAccount { get; set; } = "";
    [Required, StringLength(120)] public string DisplayName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    [StringLength(254), EmailAddress] public string? Email { get; set; }
    public OrganizationRole Role { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public bool IsActive { get; set; } = true;
    public static string Normalize(string account) => account.Trim().ToUpperInvariant();
}

// Append-only rate versions. Amounts are integer kurus, never binary floating-point.
public sealed class CostTariff
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public Printer Printer { get; set; } = null!;
    public DateTime EffectiveFromUtc { get; set; }
    [Range(0, 1_000_000)] public int MonochromeKurus { get; set; }
    [Range(0, 1_000_000)] public int ColorKurus { get; set; }
}

public enum CostState { NotCalculated, Calculated, MissingMetadata, NoTariff, NotChargeable }

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Actor { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string Action { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
