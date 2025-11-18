using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations.Consts;

public class CompanyConfigration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasKey(rc => rc.Id);
        builder.Property(rc => rc.Name).IsRequired().HasMaxLength(50);
        builder.Property(rc => rc.Address).HasMaxLength(100);
        builder.Property(rc => rc.Phone).HasMaxLength(20);
        builder.Property(rc => rc.Email).HasMaxLength(50);

    }

}
