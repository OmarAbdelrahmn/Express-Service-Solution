// ─────────────────────────────────────────────────────────────────────────────
// EF Core configuration for Company2ValidationConfig
// Add this class to your Infrastructure / Persistence layer,
// then register it in OnModelCreating:
//   modelBuilder.ApplyConfiguration(new Company2ValidationConfigConfiguration());
// ─────────────────────────────────────────────────────────────────────────────

using Domain.Entities;
using Domain.Entities.Keeta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class Company2ValidationConfigConfiguration
    : IEntityTypeConfiguration<Company2ValidationConfig>
{
    public void Configure(EntityTypeBuilder<Company2ValidationConfig> builder)
    {
        builder.ToTable("Company2ValidationConfig");

        builder.HasKey(x => x.Id);

        // Enforce singleton: only Id = 1 is ever inserted.
        // The application layer is responsible for this invariant; the DB
        // check constraint below adds a safety net.
        builder.Property(x => x.Id)
               .ValueGeneratedNever();   // We supply Id = 1 ourselves

        builder.HasCheckConstraint("CK_Company2ValidationConfig_Singleton", "[Id] = 1");

        // Precision for float columns stored as real / float(24)
        builder.Property(x => x.TargetHoursPerDay)
               .HasColumnType("real");

        builder.Property(x => x.MinWorkingHoursPerDay)
               .HasColumnType("real");

        builder.Property(x => x.UpdatedBy)
               .HasMaxLength(100);

        // Seed the one-and-only default row so that new deployments
        // already have a config without needing a manual INSERT.
        builder.HasData(new Company2ValidationConfig
        {
            Id = 1,
            TargetOrdersPerDay = 12,
            TargetHoursPerDay = 10.5f,
            MinWorkingHoursPerDay = 10f,
            FullMonthTargetOrders = 300,
            FirstCriticalDaysCount = 3,
            LastCriticalDaysCount = 4,
            MaxStartDayForExistingRiders = 5,
            AllowedMissingDays28 = 3,
            AllowedMissingDays29 = 3,
            AllowedMissingDays30 = 4,
            AllowedMissingDays31 = 5,
            UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedBy = "System"
        });
    }
}