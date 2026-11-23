using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TelDiskCleaner.LicenseServer.Data;

public sealed class LicenseRecord
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public int MaxDevices { get; set; } = 1;
    public bool Revoked { get; set; }
    public List<ActivationRecord> Activations { get; set; } = new();
}

public sealed class ActivationRecord
{
    public int Id { get; set; }
    public int LicenseId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime ActivatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? DeactivatedUtc { get; set; }
}

public sealed class TrialRecord
{
    public string DeviceId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
}

public sealed class LicenseDb(DbContextOptions<LicenseDb> options) : DbContext(options)
{
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();
    public DbSet<ActivationRecord> Activations => Set<ActivationRecord>();
    public DbSet<TrialRecord> Trials => Set<TrialRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LicenseRecord>().HasIndex(l => l.Key).IsUnique();
        modelBuilder.Entity<LicenseRecord>().HasMany(l => l.Activations).WithOne().HasForeignKey(a => a.LicenseId);
        modelBuilder.Entity<ActivationRecord>().HasIndex(a => a.DeviceId);
        modelBuilder.Entity<TrialRecord>().HasKey(t => t.DeviceId);

        // SQLite stores dates as text without a kind; read them back as UTC.
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
            }
        }
    }
}
