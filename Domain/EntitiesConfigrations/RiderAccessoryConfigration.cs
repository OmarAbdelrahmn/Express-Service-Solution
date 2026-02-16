using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;


public class RiderAccessoryConfigration : IEntityTypeConfiguration<RiderAccessory>
{
    public void Configure(EntityTypeBuilder<RiderAccessory> builder)
    {
        builder.HasKey(ra => ra.Id);

        builder.Property(ra => ra.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ra => ra.Location)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ra => ra.Price)
            .HasColumnType("decimal(18,2)");

        builder.HasIndex(ra => ra.Location);
        builder.HasIndex(ra => ra.Name);
    }
}