using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class RiderDetailsConfigration : IEntityTypeConfiguration<RiderDetails>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<RiderDetails> builder)
    {
        builder.HasKey(rd => rd.Id);

        builder.HasIndex(rd => rd.WorkingId);

        builder.HasIndex(rd => rd.EmployeeIqamaNo);

        builder.HasIndex(rd => rd.VehicleNumber);

        builder.HasIndex(rd => rd.CompanyId);

    }
}
