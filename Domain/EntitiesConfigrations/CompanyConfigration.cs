using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class CompanyConfigration : IEntityTypeConfiguration<Company>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Company> builder)
    {
        builder.HasIndex(c => c.Name).IsUnique();
        
    }
}
