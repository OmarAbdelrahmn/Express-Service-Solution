using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class OutRiderInfoConfigration : IEntityTypeConfiguration<OutRiderInfo>
{
    public void Configure(EntityTypeBuilder<OutRiderInfo> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RiderId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.PhoneNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(r => r.CreatedBy)
            .HasMaxLength(100);

        builder.HasIndex(r => r.RiderId)
            .IsUnique();

        builder.HasIndex(r => r.PhoneNumber);
    }
}
