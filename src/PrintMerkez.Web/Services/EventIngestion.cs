using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Services;

public static class EventIngestion
{
    public static async Task<IResult> AcceptAsync(PrintEventRequest request, PrintDbContext db)
    {
        var validation = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), validation, true);
        var errors = validation.SelectMany(v => v.MemberNames.Select(m => (Name: m, Error: v.ErrorMessage!)))
            .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.Select(x => x.Error).ToArray());
        if (request.OccurredAt == default || request.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
            errors[nameof(request.OccurredAt)] = ["Geçerli bir olay zamanı gerekli; 5 dakikadan fazla gelecek zaman kabul edilmez."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        request.Server = request.Server.Trim().ToUpperInvariant();
        request.EventId = request.EventId.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Server == request.Server && x.EventId == request.EventId);
        if (existing is not null) return Results.Ok(new { existing.Id, Duplicate = true });
        var printer = await db.Printers.FindAsync(request.PrinterId);
        if (printer is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["PrinterId"] = ["Yazıcı envanterde bulunamadı."] });
        var job = new PrintJob
        {
            EventId = request.EventId, Server = request.Server, PrinterId = printer.Id, LocationId = printer.LocationId,
            Account = request.Account.Trim(), OccurredAtUtc = request.OccurredAt.UtcDateTime,
            Impressions = request.Impressions, Copies = request.Copies, Color = request.Color,
            ReportedDocumentPages = request.ReportedDocumentPages,
            RequestedColor = request.RequestedColor,
            Status = request.Status, Source = request.Source
        };
        await CostCalculator.EnrichAsync(job, db);
        db.Jobs.Add(job);
        try
        {
            await db.SaveChangesAsync();
            await UsageAlertService.EvaluateJobAsync(job, db);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            existing = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Server == request.Server && x.EventId == request.EventId);
            if (existing is null) throw;
            return Results.Ok(new { existing.Id, Duplicate = true });
        }
        return Results.Created($"/Jobs?Search={Uri.EscapeDataString(request.EventId)}", new { job.Id, Duplicate = false });
    }
}
