using Domain.Entities.Vacation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Domain.EntitiesConfigrations;

public class VacationUserRoleAssignmentConfigration : IEntityTypeConfiguration<VacationUserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<VacationUserRoleAssignment> e)
    {
        e.ToTable("VacationUserRoleAssignments");
        e.HasKey(x => new { x.UserId, x.Role });
        e.Property(x => x.UserId).HasMaxLength(450);
        e.Property(x => x.Role).HasConversion<int>();
        e.Property(x => x.GrantedBy).IsRequired().HasMaxLength(450);
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VacationRequestConfigration : IEntityTypeConfiguration<VacationRequest>
{
    public void Configure(EntityTypeBuilder<VacationRequest> e)
    {
        e.ToTable("VacationRequests", t => t.HasCheckConstraint("CK_VacationRequests_DateRange", "[EndDate] >= [StartDate]"));
        e.Property(x => x.MemberNotes).HasMaxLength(1000);
        e.Property(x => x.RequestedByUserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.RequestedByName).IsRequired().HasMaxLength(200);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.HrStatus).HasConversion<int>();
        e.Property(x => x.CancelledByUserId).HasMaxLength(450);
        e.Property(x => x.CancelledByName).HasMaxLength(200);
        e.Property(x => x.CancellationReason).HasMaxLength(1000);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.RiderId, x.Status, x.StartDate, x.EndDate });
        e.HasIndex(x => new { x.Status, x.RequestedAt });
        e.HasOne(x => x.Rider).WithMany().HasForeignKey(x => x.RiderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VacationHrDocumentConfigration : IEntityTypeConfiguration<VacationHrDocument>
{
    public void Configure(EntityTypeBuilder<VacationHrDocument> e)
    {
        e.ToTable("VacationHrDocuments");
        e.Property(x => x.Type).HasConversion<int>();
        e.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(260);
        e.Property(x => x.StoredRelativePath).IsRequired().HasMaxLength(1000);
        e.Property(x => x.ContentType).IsRequired().HasMaxLength(100);
        e.Property(x => x.UploadedByUserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.UploadedByName).IsRequired().HasMaxLength(200);
        e.Property(x => x.SupersededByUserId).HasMaxLength(450);
        e.Property(x => x.SupersededReason).HasMaxLength(1000);
        e.HasIndex(x => new { x.VacationRequestId, x.Type, x.Version }).IsUnique();
        e.HasIndex(x => new { x.VacationRequestId, x.Type, x.IsSuperseded });
        e.HasOne(x => x.VacationRequest)
            .WithMany(x => x.HrDocuments)
            .HasForeignKey(x => x.VacationRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class VacationApprovalDecisionConfigration : IEntityTypeConfiguration<VacationApprovalDecision>
{
    public void Configure(EntityTypeBuilder<VacationApprovalDecision> e)
    {
        e.ToTable("VacationApprovalDecisions");
        e.Property(x => x.Role).HasConversion<int>();
        e.Property(x => x.Decision).HasConversion<int>();
        e.Property(x => x.TargetRole).HasConversion<int?>();
        e.Property(x => x.IsSuperseded).HasDefaultValue(false);
        e.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        e.Property(x => x.DecidedByUserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.DecidedByName).IsRequired().HasMaxLength(200);
        e.HasIndex(x => new { x.VacationRequestId, x.Role })
            .IsUnique()
            .HasFilter("[Decision] = 1 AND [IsSuperseded] = 0");
        e.HasIndex(x => new { x.VacationRequestId, x.DecidedAt });
        e.HasOne(x => x.VacationRequest).WithMany(x => x.Decisions).HasForeignKey(x => x.VacationRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VacationDateChangeRequestConfigration : IEntityTypeConfiguration<VacationDateChangeRequest>
{
    public void Configure(EntityTypeBuilder<VacationDateChangeRequest> e)
    {
        e.ToTable("VacationDateChangeRequests", t => t.HasCheckConstraint("CK_VacationDateChangeRequests_DateRange", "[ProposedEndDate] >= [ProposedStartDate]"));
        e.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        e.Property(x => x.RequestedByUserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.RequestedByName).IsRequired().HasMaxLength(200);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.ResolvedByUserId).HasMaxLength(450);
        e.Property(x => x.ResolvedByName).HasMaxLength(200);
        e.Property(x => x.ResolutionReason).HasMaxLength(1000);
        e.HasIndex(x => new { x.VacationRequestId, x.Status });
        e.HasIndex(x => x.VacationRequestId).IsUnique().HasFilter("[Status] = 1");
        e.HasOne(x => x.VacationRequest).WithMany(x => x.DateChangeRequests).HasForeignKey(x => x.VacationRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VacationCancellationRequestConfigration : IEntityTypeConfiguration<VacationCancellationRequest>
{
    public void Configure(EntityTypeBuilder<VacationCancellationRequest> e)
    {
        e.ToTable("VacationCancellationRequests");
        e.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
        e.Property(x => x.RequestedByUserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.RequestedByName).IsRequired().HasMaxLength(200);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.ResolvedByUserId).HasMaxLength(450);
        e.Property(x => x.ResolvedByName).HasMaxLength(200);
        e.Property(x => x.ResolutionReason).HasMaxLength(1000);
        e.HasIndex(x => new { x.VacationRequestId, x.Status });
        e.HasIndex(x => x.VacationRequestId).IsUnique().HasFilter("[Status] = 1");
        e.HasOne(x => x.VacationRequest).WithMany(x => x.CancellationRequests).HasForeignKey(x => x.VacationRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}
