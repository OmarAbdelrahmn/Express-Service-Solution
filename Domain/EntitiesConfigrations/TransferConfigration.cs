using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class TransferConfigration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.FromLocation)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.ToLocation)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.TransferredBy)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(t => t.HousingId);
        builder.HasIndex(t => t.TransferredAt);
    }
}
