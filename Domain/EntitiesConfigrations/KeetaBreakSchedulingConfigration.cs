using Domain.Entities.Keeta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class KeetaBreakConfigurationConfigration : IEntityTypeConfiguration<KeetaBreakConfiguration>
{
    public void Configure(EntityTypeBuilder<KeetaBreakConfiguration> e)
    {
        e.ToTable("KeetaBreakConfigurations");
        e.Property(x => x.BreakPercentage).HasPrecision(5, 2);
        e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.RoundingPolicy).HasConversion<int>();
        e.HasIndex(x => new { x.IsActive, x.EffectiveFrom });
        e.HasMany(x => x.ShiftDefinitions).WithOne(x => x.Configuration).HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.ShiftPatterns).WithOne(x => x.Configuration).HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class KeetaBreakShiftPatternConfigration : IEntityTypeConfiguration<KeetaBreakShiftPattern>
{
    public void Configure(EntityTypeBuilder<KeetaBreakShiftPattern> e)
    {
        e.ToTable("KeetaBreakShiftPatterns");
        e.Property(x => x.PatternKey).IsRequired().HasMaxLength(200);
        e.Property(x => x.ShiftKeysJson).IsRequired();
        e.Property(x => x.RiderCount).IsRequired();
        e.ToTable("KeetaBreakShiftPatterns", t => t.HasCheckConstraint("CK_KeetaBreakShiftPatterns_RiderCount", "[RiderCount] > 0"));
        e.HasIndex(x => new { x.ConfigurationId, x.PatternKey }).IsUnique();
    }
}

public class KeetaBreakShiftDefinitionConfigration : IEntityTypeConfiguration<KeetaBreakShiftDefinition>
{
    public void Configure(EntityTypeBuilder<KeetaBreakShiftDefinition> e)
    {
        e.ToTable("KeetaBreakShiftDefinitions", t => t.HasCheckConstraint("CK_KeetaBreakShiftDefinitions_Staffing", "[MinimumRiders] >= 0 AND [MaximumRiders] >= [MinimumRiders]"));
        e.Property(x => x.ShiftKey).IsRequired().HasMaxLength(20);
        e.HasIndex(x => new { x.ConfigurationId, x.ShiftKey }).IsUnique();
    }
}

public class KeetaBreakBatchConfigration : IEntityTypeConfiguration<KeetaBreakBatch>
{
    public void Configure(EntityTypeBuilder<KeetaBreakBatch> e)
    {
        e.ToTable("KeetaBreakBatches", t => t.HasCheckConstraint("CK_KeetaBreakBatches_DateRange", "[PeriodEnd] >= [PeriodStart]"));
        e.Property(x => x.SourceFileName).IsRequired().HasMaxLength(260);
        e.Property(x => x.ImportedBy).IsRequired().HasMaxLength(450);
        e.Property(x => x.ConfirmedBy).HasMaxLength(450);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.PeriodStart, x.PeriodEnd, x.Status });
        e.HasOne(x => x.Configuration).WithMany().HasForeignKey(x => x.ConfigurationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class KeetaBreakImportedRiderConfigration : IEntityTypeConfiguration<KeetaBreakImportedRider>
{
    public void Configure(EntityTypeBuilder<KeetaBreakImportedRider> e)
    {
        e.ToTable("KeetaBreakImportedRiders");
        e.Property(x => x.RiderNumber).HasMaxLength(100);
        e.Property(x => x.RiderIdentifier).IsRequired().HasMaxLength(150);
        e.Property(x => x.RiderName).HasMaxLength(300);
        e.Property(x => x.HousingGroup).HasMaxLength(300);
        e.HasIndex(x => new { x.BatchId, x.RiderIdentifier }).IsUnique();
        e.HasOne(x => x.Batch).WithMany(x => x.Riders).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class KeetaBreakAssignmentConfigration : IEntityTypeConfiguration<KeetaBreakAssignment>
{
    public void Configure(EntityTypeBuilder<KeetaBreakAssignment> e)
    {
        e.ToTable("KeetaBreakAssignments");
        e.Property(x => x.RiderIdentifier).IsRequired().HasMaxLength(150);
        e.Property(x => x.Status).HasConversion<int>();
        e.HasIndex(x => new { x.RiderIdentifier, x.BreakDate }).IsUnique().HasFilter("[Status] = 2");
        e.HasIndex(x => new { x.BreakDate, x.Status });
        e.HasOne(x => x.Batch).WithMany(x => x.Assignments).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}
