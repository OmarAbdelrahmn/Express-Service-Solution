using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class RiderShiftSubstitutionConfigration : IEntityTypeConfiguration<RiderShiftSubstitution>
{
    public void Configure(EntityTypeBuilder<RiderShiftSubstitution> builder)
    {
        builder.HasKey(rss => rss.Id);
        builder.Property(rss => rss.ActualRiderId)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(rss => rss.SubstituteWorkingId)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(rss => rss.StartDate)
            .IsRequired();
        builder.Property(rss => rss.EndDate)
            .IsRequired();
        builder.Property(rss => rss.Reason)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(rss => rss.CreatedBy)
            .HasMaxLength(100);
    }
}
