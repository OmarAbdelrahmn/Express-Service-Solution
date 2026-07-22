using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class InventoryAuditLogConfigration : IEntityTypeConfiguration<InventoryAuditLog>
{
    public void Configure(EntityTypeBuilder<InventoryAuditLog> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ItemName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.PerformedBy)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.LocationBefore)
            .HasMaxLength(50);

        builder.Property(a => a.LocationAfter)
            .HasMaxLength(50);

        builder.Property(a => a.PriceBefore)
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.PriceAfter)
            .HasColumnType("decimal(18,2)");

        // Fast lookups for "everything that happened to this item"
        builder.HasIndex(a => new { a.ItemType, a.ItemId });

        // Fast lookups for the member (housing-scoped) audit endpoint
        builder.HasIndex(a => a.LocationBefore);
        builder.HasIndex(a => a.LocationAfter);

        // Fast lookups for the main-service "all changes" endpoint
        builder.HasIndex(a => a.PerformedAt);
    }
}
