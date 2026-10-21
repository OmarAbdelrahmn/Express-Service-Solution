using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class RiderMobileSessionConfiguration : IEntityTypeConfiguration<RiderMobileSession>
{
    public void Configure(EntityTypeBuilder<RiderMobileSession> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Language).HasMaxLength(2);
        builder.HasIndex(x => new { x.RiderId, x.ExpiresAtUtc });
        builder.HasOne(x => x.Rider).WithMany().HasForeignKey(x => x.RiderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
