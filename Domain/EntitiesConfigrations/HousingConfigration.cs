using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class HousingConfigration :IEntityTypeConfiguration<Housing>
{
    public void Configure(EntityTypeBuilder<Housing> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(h => h.Address)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(h => h.Capacity)
            .IsRequired();

    }

}
