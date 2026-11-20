using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
namespace PrintMerkez.Web.Data;

public sealed class PrintDbContext(DbContextOptions<PrintDbContext> options, IHttpContextAccessor? accessor = null) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<PrintJob> Jobs => Set<PrintJob>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DirectoryUser> DirectoryUsers => Set<DirectoryUser>();
    public DbSet<CostTariff> Tariffs => Set<CostTariff>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ManagerAssignment> ManagerAssignments => Set<ManagerAssignment>();
    public DbSet<UsageAlertPolicy> UsageAlertPolicies => Set<UsageAlertPolicy>();
    public DbSet<UsageNotification> UsageNotifications => Set<UsageNotification>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Location>().HasIndex(x => x.Name).IsUnique();
        model.Entity<Printer>().HasIndex(x => new { x.LocationId, x.Name }).IsUnique();
        model.Entity<PrintJob>().HasIndex(x => new { x.Server, x.EventId }).IsUnique();
        model.Entity<PrintJob>().HasIndex(x => new { x.PrinterId, x.DeviceJobKey }).IsUnique();
        model.Entity<PrintJob>().Property(x => x.DeviceJobKey).HasMaxLength(120);
        model.Entity<PrintJob>().HasIndex(x => new { x.Source, x.OccurredAtUtc });
        model.Entity<PrintJob>().HasOne(x => x.Printer).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<PrintJob>().HasOne(x => x.Location).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<Department>().HasIndex(x => x.Name).IsUnique();
        model.Entity<DirectoryUser>().HasIndex(x => x.NormalizedAccount).IsUnique();
        model.Entity<DirectoryUser>().HasIndex(x => x.AdObjectGuid).IsUnique();
        model.Entity<DirectoryUser>().HasIndex(x => new { x.AdDomain, x.AdSamAccountName });
        model.Entity<DirectoryUser>().HasOne(x => x.Department).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<CostTariff>().HasIndex(x => new { x.PrinterId, x.EffectiveFromUtc }).IsUnique();
        model.Entity<CostTariff>().HasOne(x => x.Printer).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<AuditEntry>().HasIndex(x => x.OccurredAtUtc);
        model.Entity<DirectoryUser>().HasOne(x => x.Location).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<PrintJob>().HasOne<DirectoryUser>().WithMany().HasForeignKey(x => x.DirectoryUserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PrintJob>().HasIndex(x => new { x.NormalizedAccount, x.Source, x.OccurredAtUtc });
        model.Entity<UsageAlertPolicy>().HasIndex(x => x.Name).IsUnique();
        model.Entity<ManagerAssignment>().HasIndex(x => new { x.DepartmentId, x.LocationId }).IsUnique();
        model.Entity<ManagerAssignment>().HasOne(x => x.Department).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManagerAssignment>().HasOne(x => x.Location).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManagerAssignment>().HasOne(x => x.ManagerUser).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<UsageNotification>().HasIndex(x => new { x.PolicyId, x.UserId, x.PeriodKey, x.Source }).IsUnique();
        model.Entity<UsageNotification>().HasIndex(x => new { x.State, x.NextAttemptAtUtc });
        model.Entity<UsageNotification>().HasOne(x => x.Policy).WithMany().OnDelete(DeleteBehavior.Restrict);
        model.Entity<UsageNotification>().HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Restrict);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new NotSupportedException("Denetimli kayıt için SaveChangesAsync kullanın.");

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => SaveChangesAsync(true, cancellationToken);
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var added in ChangeTracker.Entries<PrintJob>().Where(e => e.State == EntityState.Added))
            added.Entity.NormalizedAccount = DirectoryUser.Normalize(added.Entity.Account);
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Denetim kayıtları uygulamadan değiştirilemez veya silinemez.");
        if (ChangeTracker.Entries<CostTariff>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Geçmiş tarife değiştirilemez. Yeni geçerlilik tarihli tarife ekleyin.");
        var changes = ChangeTracker.Entries().Where(e =>
            (e.Entity is Location or Printer or Department or DirectoryUser or CostTariff or ManagerAssignment or UsageAlertPolicy) &&
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new
            {
                Entry = e, Action = e.State.ToString(),
                Before = e.State == EntityState.Added ? null : JsonSerializer.Serialize(e.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue))
            }).ToList();
        if (changes.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (!acceptAllChangesOnSuccess) throw new NotSupportedException("Denetimli kayıt için acceptAllChangesOnSuccess=true gerekir.");
        await using var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var count = await base.SaveChangesAsync(true, cancellationToken);
        var http = accessor?.HttpContext;
        var actor = http is null ? "Sistem başlangıcı" : http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name ?? "Kimliği doğrulanan kullanıcı" : "Yerel geliştirme / kimlik doğrulanmadı";
        foreach (var change in changes)
        {
            AuditEntries.Add(new AuditEntry
            {
                OccurredAtUtc = DateTime.UtcNow, Actor = actor, Entity = change.Entry.Metadata.ClrType.Name,
                EntityKey = string.Join(",", change.Entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue)),
                Action = change.Action, BeforeJson = change.Before,
                AfterJson = change.Action == "Deleted" ? null : JsonSerializer.Serialize(change.Entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))
            });
        }
        await base.SaveChangesAsync(true, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return count;
    }
}
