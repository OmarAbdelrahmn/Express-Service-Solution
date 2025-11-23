using DocumentFormat.OpenXml.Vml.Office;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class TempRiderShiftComparisonConfigrations : IEntityTypeConfiguration<TempRiderShiftComparison>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TempRiderShiftComparison> entity)
    {
        entity.HasKey(e => e.Id);

        entity.Property(e => e.ShiftDate)
            .IsRequired();

        entity.Property(e => e.WorkingId)
            .IsRequired();

        entity.Property(e => e.IsSubstitution)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.OriginalRiderWorkingId)
            .IsRequired(false);

        entity.Property(e => e.NewShiftStatus)
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(e => e.OldShiftStatus)
            .HasMaxLength(50);



        // Relationships
        entity.HasOne(e => e.Rider)
            .WithMany()
            .HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for performance
        entity.HasIndex(e => new { e.ShiftDate, e.WorkingId });
        entity.HasIndex(e => new { e.RiderId, e.WorkingId, e.ShiftDate });
        entity.HasIndex(e => e.IsResolved);
        entity.HasIndex(e => e.IsSubstitution);
    }
}
