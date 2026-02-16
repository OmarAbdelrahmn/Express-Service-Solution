using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;


public class SparePartUsageConfigration : IEntityTypeConfiguration<SparePartUsage>
{
    public void Configure(EntityTypeBuilder<SparePartUsage> builder)
    {
        builder.HasKey(spu => spu.Id);

        builder.HasOne(spu => spu.SparePart)
            .WithMany(sp => sp.SparePartUsages)
            .HasForeignKey(spu => spu.SparePartId);

        builder.Property(c => c.Cost).HasColumnType("decimal(18,2)");

        builder.HasOne(spu => spu.Vehicle)
            .WithMany()
            .HasForeignKey(spu => spu.VehicleNumber)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(spu => spu.VehicleNumber);
        builder.HasIndex(spu => spu.SparePartId);
        builder.HasIndex(spu => spu.UsedAt);
    }
}
