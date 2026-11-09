using System.ComponentModel.DataAnnotations;

namespace PrintMerkez.Core;

public sealed class Location
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    public bool IsPlaceholder { get; set; }
}

public sealed class Printer
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Required, StringLength(100)] public string Model { get; set; } = "";
    [StringLength(45)] public string? IpAddress { get; set; }
    [StringLength(120)] public string? QueueName { get; set; }
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
}

public enum PrintJobStatus { QueueCompleted, Cancelled, Failed, Unknown }
public enum PrintColor { Unknown, Monochrome, Color }
public enum EventSource { Simulation, WindowsPrintService }

public sealed class PrintJob
{
    public long Id { get; set; }
    public string EventId { get; set; } = "";
    public string Server { get; set; } = "";
    public int PrinterId { get; set; }
    public Printer Printer { get; set; } = null!;
    // Preserve the historical location when a printer is moved.
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
    public string Account { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    // Total reported impressions including copies, not physical sheets. Null = unknown.
    public int? Impressions { get; set; }
    public int? Copies { get; set; }
    public PrintColor Color { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Unknown;
    public EventSource Source { get; set; }
}

public sealed class PrintEventRequest
{
    [Required, StringLength(120)] public string EventId { get; set; } = "";
    [Required, StringLength(80)] public string Server { get; set; } = "";
    [Range(1, int.MaxValue)] public int PrinterId { get; set; }
    [Required, StringLength(160)] public string Account { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    [Range(1, 1_000_000)] public int? Impressions { get; set; }
    [Range(1, 100_000)] public int? Copies { get; set; }
    [EnumDataType(typeof(PrintColor))] public PrintColor Color { get; set; }
    [EnumDataType(typeof(PrintJobStatus))] public PrintJobStatus Status { get; set; } = PrintJobStatus.Unknown;
    [EnumDataType(typeof(EventSource))] public EventSource Source { get; set; }
}

public static class PrintLabels
{
    public static string Display(this PrintJobStatus value) => value switch
    {
        PrintJobStatus.QueueCompleted => "Kuyruk tamamlandı", PrintJobStatus.Cancelled => "İptal edildi",
        PrintJobStatus.Failed => "Hata", _ => "Bilinmiyor"
    };
    public static string Display(this PrintColor value) => value switch
    {
        PrintColor.Monochrome => "Siyah-beyaz", PrintColor.Color => "Renkli", _ => "Bilinmiyor"
    };
    public static DateTime LocalTime(this PrintJob job) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(job.OccurredAtUtc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
}
