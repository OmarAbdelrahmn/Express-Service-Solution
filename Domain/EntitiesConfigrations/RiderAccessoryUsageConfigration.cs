using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;


public class RiderAccessoryUsageConfigration : IEntityTypeConfiguration<RiderAccessoryUsage>
{
    public void Configure(EntityTypeBuilder<RiderAccessoryUsage> builder)
    {
        builder.HasKey(rau => rau.Id);



        builder.HasOne(rau => rau.RiderAccessory)
            .WithMany(ra => ra.RiderAccessoryUsages)
            .HasForeignKey(rau => rau.RiderAccessoryId);

        builder.Property(c => c.Cost).HasColumnType("decimal(18,2)");


        builder.HasOne(rau => rau.Rider)
            .WithMany()
            .HasForeignKey(rau => rau.RiderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(rau => rau.RiderId);
        builder.HasIndex(rau => rau.IssuedAt);
        builder.HasIndex(rau => rau.RiderAccessoryId);
    }
}