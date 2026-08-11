namespace Domain.Entities.Vacation;

public enum VacationRole
{
    Operation = 1,
    Accountant = 2,
    Administration = 3,
    HR = 4,
    KeetaManager = 5
}

public enum VacationRequestStatus
{
    PendingOperation = 1,
    PendingAccountant = 2,
    PendingAdministration = 3,
    Approved = 4,
    Active = 5,
    Completed = 6,
    Rejected = 7,
    Cancelled = 8,
    Expired = 9,
    PendingKeetaManager = 10
}

public enum VacationDecision
{
    Approved = 1,
    Rejected = 2,
    Returned = 3
}

public enum VacationAmendmentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Superseded = 4
}

public enum VacationHrStatus
{
    PendingApproval = 0,
    AwaitingTicket = 1,
    AwaitingExitReentryVisa = 2,
    Completed = 3,
    Closed = 4
}

public enum VacationHrDocumentType
{
    Ticket = 1,
    ExitReentryVisa = 2
}

/// <summary>Vacation-only authorization; it deliberately does not modify ASP.NET Identity roles.</summary>
public class VacationUserRoleAssignment
{
    public string UserId { get; set; } = string.Empty;
    public VacationRole Role { get; set; }
    public string GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public ApplicationUser User { get; set; } = null!;
}

public class VacationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RiderId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? MemberNotes { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public VacationRequestStatus Status { get; set; } = VacationRequestStatus.PendingOperation;
    public DateTime? FullyApprovedAt { get; set; }
    public VacationHrStatus HrStatus { get; set; } = VacationHrStatus.PendingApproval;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByUserId { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancellationReason { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public RiderDetails Rider { get; set; } = null!;
    public ICollection<VacationApprovalDecision> Decisions { get; set; } = [];
    public ICollection<VacationDateChangeRequest> DateChangeRequests { get; set; } = [];
    public ICollection<VacationCancellationRequest> CancellationRequests { get; set; } = [];
    public ICollection<VacationHrDocument> HrDocuments { get; set; } = [];
}

public class VacationApprovalDecision
{
    public long Id { get; set; }
    public Guid VacationRequestId { get; set; }
    public VacationRole Role { get; set; }
    public VacationDecision Decision { get; set; }
    public VacationRole? TargetRole { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DecidedByUserId { get; set; } = string.Empty;
    public string DecidedByName { get; set; } = string.Empty;
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public bool IsSuperseded { get; set; }
    public DateTime? SupersededAt { get; set; }
    public VacationRequest VacationRequest { get; set; } = null!;
}

public class VacationDateChangeRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacationRequestId { get; set; }
    public DateOnly PreviousStartDate { get; set; }
    public DateOnly PreviousEndDate { get; set; }
    public DateOnly ProposedStartDate { get; set; }
    public DateOnly ProposedEndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public VacationAmendmentStatus Status { get; set; } = VacationAmendmentStatus.Pending;
    public string? ResolvedByUserId { get; set; }
    public string? ResolvedByName { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public VacationRequest VacationRequest { get; set; } = null!;
}

public class VacationCancellationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacationRequestId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public VacationAmendmentStatus Status { get; set; } = VacationAmendmentStatus.Pending;
    public string? ResolvedByUserId { get; set; }
    public string? ResolvedByName { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public VacationRequest VacationRequest { get; set; } = null!;
}

/// <summary>
/// Versioned HR artifact. Replaced ticket/visa files remain immutable audit history
/// while the latest non-superseded row represents the current task file.
/// </summary>
public class VacationHrDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacationRequestId { get; set; }
    public VacationHrDocumentType Type { get; set; }
    public int Version { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredRelativePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsSuperseded { get; set; }
    public DateTime? SupersededAt { get; set; }
    public string? SupersededByUserId { get; set; }
    public string? SupersededReason { get; set; }
    public VacationRequest VacationRequest { get; set; } = null!;
}
