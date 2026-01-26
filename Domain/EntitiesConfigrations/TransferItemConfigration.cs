using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.EntitiesConfigrations;

public class TransferItemConfigration : IEntityTypeConfiguration<TransferItem>
{
    public void Configure(EntityTypeBuilder<TransferItem> builder)
    {
        builder.HasKey(ti => ti.Id);

        builder.Property(ti => ti.ItemName)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasOne(ti => ti.Transfer)
            .WithMany(t => t.TransferItems)
            .HasForeignKey(ti => ti.TransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ti => new { ti.TransferId, ti.ItemType });
    }
}