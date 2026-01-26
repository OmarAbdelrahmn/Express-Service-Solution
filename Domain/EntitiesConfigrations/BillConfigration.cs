using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class BillConfigration : IEntityTypeConfiguration<Bill>
{
    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.InvoiceNumber)
            .HasMaxLength(100);

        builder.Property(b => b.ProcessedBy)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(b => b.TotalAmount)
            .HasColumnType("decimal(18,2)");

        builder.HasOne(b => b.Supplier)
            .WithMany(s => s.Bills)
            .HasForeignKey(b => b.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.SupplierId);
        builder.HasIndex(b => b.InvoiceNumber);
        builder.HasIndex(b => b.ProcessedAt);
    }
}