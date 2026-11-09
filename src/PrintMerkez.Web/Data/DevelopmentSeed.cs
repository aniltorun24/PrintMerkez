using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;

namespace PrintMerkez.Web.Data;

public static class DevelopmentSeed
{
    private static readonly string[] LocationNames =
    [
        "5. Levent", "Bulvar Samsun AVM", "Forum Kayseri", "Forum Mersin",
        "Korupark AVM", "Mall of Antalya", "Mall of İstanbul", "Sera Kütahya AVM",
        "Torium AVM", "Torun Center", "Zafer Plaza"
    ];

    public static async Task InitializeAsync(PrintDbContext db, bool demo)
    {
        await db.Database.EnsureCreatedAsync();
        if (!demo) return;
        var existing = await db.Locations.ToListAsync();
        if (existing.Count > 0)
        {
            // Upgrade only untouched placeholders; retain IDs, user edits and job relationships.
            for (var i = 0; i < LocationNames.Length; i++)
            {
                var placeholder = existing.SingleOrDefault(x => x.IsPlaceholder && x.Name == $"Lokasyon {i + 1:00}");
                if (placeholder is null || existing.Any(x => x.Name == LocationNames[i])) continue;
                placeholder.Name = LocationNames[i];
                placeholder.IsPlaceholder = false;
            }
            await db.SaveChangesAsync();
            return;
        }
        var locations = LocationNames.Select(name => new Location { Name = name }).ToArray();
        db.Locations.AddRange(locations);
        var printer = new Printer { Name = "Pilot Olivetti", Model = "Olivetti d-COPIA 255MF KX", Location = locations[0] };
        db.Printers.Add(printer);
        await db.SaveChangesAsync();
        for (var i = 0; i < 12; i++)
            db.Jobs.Add(new PrintJob
            {
                EventId = $"seed-{i}", Server = "SIMULATOR", PrinterId = printer.Id, LocationId = printer.LocationId,
                Account = $"ORNEK\\kullanici{(i % 4) + 1}", OccurredAtUtc = DateTime.UtcNow.AddMinutes(-i * 23),
                Impressions = i % 5 == 0 ? null : (i + 1) * 2, Copies = 1, Color = PrintColor.Unknown,
                Source = EventSource.Simulation, Status = i == 3 ? PrintJobStatus.Failed : PrintJobStatus.QueueCompleted
            });
        await db.SaveChangesAsync();
    }
}
