using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;


public class SparePartConfigration : IEntityTypeConfiguration<SparePart>
{
    public void Configure(EntityTypeBuilder<SparePart> builder)
    {
        builder.HasKey(sp => sp.Id);

        builder.Property(sp => sp.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(sp => sp.Location)
            .IsRequired()
            .HasMaxLength(50);


        builder.Property(sp => sp.Price)
            .HasColumnType("decimal(18,2)");

        builder.HasIndex(sp => sp.Name);
        builder.HasIndex(sp => sp.Location);
    }

}