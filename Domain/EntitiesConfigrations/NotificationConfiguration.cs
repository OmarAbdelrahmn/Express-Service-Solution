using Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class RiderPushDeviceConfiguration : IEntityTypeConfiguration<RiderPushDevice>
{
    public void Configure(EntityTypeBuilder<RiderPushDevice> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.ProtectedDestination).HasMaxLength(2048);
        b.Property(x => x.DestinationHash).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.Platform).HasMaxLength(16);
        b.Property(x => x.Permission).HasMaxLength(16);
        b.Property(x => x.AppVersion).HasMaxLength(64);
        b.HasIndex(x => x.InstallationId).IsUnique();
        b.HasIndex(x => x.DestinationHash).IsUnique().HasFilter("[IsActive] = 1");
        b.HasIndex(x => new { x.RiderId, x.IsActive });
        b.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class NotificationRequestConfiguration : IEntityTypeConfiguration<NotificationRequest>
{
    public void Configure(EntityTypeBuilder<NotificationRequest> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.ActorId).HasMaxLength(450);
        b.Property(x => x.IdempotencyScope).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.EventKey).HasMaxLength(200);
        b.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false);
        b.HasIndex(x => x.IdempotencyScope).IsUnique().HasFilter("[IdempotencyScope] IS NOT NULL");
        b.HasIndex(x => x.EventKey).IsUnique().HasFilter("[EventKey] IS NOT NULL");
        b.HasIndex(x => new { x.ExpansionComplete, x.LeaseUntilUtc, x.CreatedAtUtc });
    }
}

public class NotificationRecipientConfiguration : IEntityTypeConfiguration<NotificationRecipient>
{
    public void Configure(EntityTypeBuilder<NotificationRecipient> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.RequestId, x.RiderId }).IsUnique();
        b.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.Status).HasMaxLength(16);
        b.Property(x => x.ProviderMessageId).HasMaxLength(512);
        b.Property(x => x.LastErrorCode).HasMaxLength(64);
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.LeaseUntilUtc });
        b.HasIndex(x => new { x.RecipientId, x.DeviceId, x.DeviceGeneration }).IsUnique();
        b.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
