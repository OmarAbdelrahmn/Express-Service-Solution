using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class VehicleConfigration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.HasKey(e => e.VehicleNumber);
        builder.Property(e => e.VehicleNumber).ValueGeneratedNever();
        builder.HasIndex(e => e.VehicleNumber);
        builder.HasIndex(e => e.PlateNumberA);
        builder.HasIndex(e => e.SerialNumber);
    }

}
