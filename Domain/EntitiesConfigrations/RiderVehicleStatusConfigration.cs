using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class RiderVehicleStatusConfigration : IEntityTypeConfiguration<RiderVehicleStatus>
{
    public void Configure(EntityTypeBuilder<RiderVehicleStatus> builder)
    {
        builder
           .HasIndex(s => new { s.VehicleNumber, s.IsActive });
    }
}
