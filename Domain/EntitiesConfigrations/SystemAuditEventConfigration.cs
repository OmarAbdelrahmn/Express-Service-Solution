using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class SystemAuditEventConfigration : IEntityTypeConfiguration<SystemAuditEvent>
{
    public void Configure(EntityTypeBuilder<SystemAuditEvent> builder)
    {
        builder.ToTable("SystemAuditEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.ActorName).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ActorUserId).HasMaxLength(450);
        builder.Property(x => x.Source).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OperationName).HasMaxLength(256);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.HttpMethod).HasMaxLength(16);
        builder.Property(x => x.RequestPath).HasMaxLength(1024);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(512);
        builder.Property(x => x.EntityKey).IsRequired().HasMaxLength(900);
        builder.Property(x => x.EntityDisplayName).HasMaxLength(512);
        builder.Property(x => x.ChangedFieldsJson).IsRequired();
        builder.Property(x => x.ScopeType).HasMaxLength(64);
        builder.Property(x => x.ScopeBefore).HasMaxLength(256);
        builder.Property(x => x.ScopeAfter).HasMaxLength(256);

        builder.HasIndex(x => new { x.OccurredAtUtc, x.Id });
        builder.HasIndex(x => new { x.EntityType, x.EntityKey, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OperationId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ScopeType, x.ScopeBefore, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ScopeType, x.ScopeAfter, x.OccurredAtUtc });
    }
}
