using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class RiderScorePerformanceConfigration : IEntityTypeConfiguration<RiderScorePerformance>
{
    public void Configure(EntityTypeBuilder<RiderScorePerformance> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkingId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SourceRiderId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SubstituteRiderName).HasMaxLength(200);
        builder.Property(x => x.Segment).IsRequired().HasMaxLength(50);
        builder.Property(x => x.VerificationSuccessRate).HasPrecision(18, 10);
        builder.Property(x => x.OnTimeDeliveryScore).HasPrecision(18, 10);
        builder.Property(x => x.FinalDeliveryQualityScore).HasPrecision(18, 10);

        builder.HasIndex(x => x.RiderId);
        builder.HasIndex(x => x.PerformanceDate);
        builder.HasIndex(x => new { x.RiderId, x.PerformanceDate }).IsUnique();
        builder.HasIndex(x => x.SourceRiderId);

        builder.HasOne(x => x.Rider)
            .WithMany()
            .HasForeignKey(x => x.RiderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
