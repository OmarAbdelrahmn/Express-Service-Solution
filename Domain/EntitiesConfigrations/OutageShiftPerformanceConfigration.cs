using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class OutageShiftPerformanceConfigration : IEntityTypeConfiguration<OutageShiftPerformance>
{
    public void Configure(EntityTypeBuilder<OutageShiftPerformance> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UploadedBy)
            .HasMaxLength(100);

        builder.HasOne(s => s.OutRiderInfo)
            .WithMany(r => r.OutageShiftPerformances)
            .HasForeignKey(s => s.OutRiderInfoId);

        builder.HasIndex(s => s.OutRiderInfoId);
        builder.HasIndex(s => s.ShiftDate);
        builder.HasIndex(s => new { s.OutRiderInfoId, s.ShiftDate })
            .IsUnique();
    }
}
