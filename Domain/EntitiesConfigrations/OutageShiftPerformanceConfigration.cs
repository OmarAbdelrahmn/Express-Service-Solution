using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class OutageShiftPerformanceConfigration : IEntityTypeConfiguration<OutageShiftPerformance>
{
    public void Configure(EntityTypeBuilder<OutageShiftPerformance> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SystemId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.PhoneNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(s => s.UploadedBy)
            .HasMaxLength(100);

        builder.HasIndex(s => s.SystemId);
        builder.HasIndex(s => s.PhoneNumber);
        builder.HasIndex(s => s.ShiftDate);
        builder.HasIndex(s => new { s.SystemId, s.ShiftDate });
    }
}
