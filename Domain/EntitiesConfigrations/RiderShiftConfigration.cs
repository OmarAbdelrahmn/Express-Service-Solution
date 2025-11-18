using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class RiderShiftConfigration : IEntityTypeConfiguration<RiderShift>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<RiderShift> builder)
    {
        builder.HasKey(rs => new { rs.RiderId, rs.ShiftDate , rs.WorkingId });

        builder.HasIndex(rs => rs.ShiftDate);
        builder.HasIndex(rs => rs.RiderId);
        builder.HasIndex(rs => rs.WorkingId);
        builder.HasIndex(rs => rs.ShiftStatus);
        builder.HasIndex(rs => rs.CompanyId);

    }
}
