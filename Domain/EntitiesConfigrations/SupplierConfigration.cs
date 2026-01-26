using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class SupplierConfigration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.ContactPerson)
            .HasMaxLength(100);

        builder.Property(s => s.Phone)
            .HasMaxLength(20);

        builder.Property(s => s.Email)
            .HasMaxLength(100);

        builder.Property(s => s.Address)
            .HasMaxLength(500);

        builder.Property(s => s.TaxNumber)
            .HasMaxLength(50);

        builder.HasIndex(s => s.Name)
            .IsUnique();

        builder.HasIndex(s => s.IsActive);
    }
}