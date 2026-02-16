using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class RiderVehicleStatusConfigration : IEntityTypeConfiguration<RiderVehicleStatus>
{
    public void Configure(EntityTypeBuilder<RiderVehicleStatus> builder)
    {
        builder
           .HasIndex(s => new { s.VehicleNumber, s.IsActive });
    }
}
