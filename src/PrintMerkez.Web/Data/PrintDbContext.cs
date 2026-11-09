using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;

namespace PrintMerkez.Web.Data;

public sealed class PrintDbContext(DbContextOptions<PrintDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<PrintJob> Jobs => Set<PrintJob>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Location>().HasIndex(x => x.Name).IsUnique();
        model.Entity<Printer>().HasIndex(x => new { x.LocationId, x.Name }).IsUnique();
        model.Entity<PrintJob>().HasIndex(x => new { x.Server, x.EventId }).IsUnique();
        model.Entity<PrintJob>().HasIndex(x => x.OccurredAtUtc);
        model.Entity<PrintJob>().HasOne(x => x.Printer).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<PrintJob>().HasOne(x => x.Location).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}
