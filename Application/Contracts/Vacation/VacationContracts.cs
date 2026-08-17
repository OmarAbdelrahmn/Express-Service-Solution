using Domain.Entities.Vacation;
using FluentValidation;

namespace Application.Contracts.Vacation;

public record CreateVacationRequest(int? RiderId, DateOnly StartDate, DateOnly EndDate, string? MemberNotes = null, long? EmployeeIqamaNo = null);
public record VacationDecisionRequest(VacationDecision Decision, string Reason, VacationRole? TargetRole = null);
public record CreateVacationDateChangeRequest(DateOnly StartDate, DateOnly EndDate, string Reason);
public record CreateVacationCancellationRequest(string Reason);
public record ResolveVacationAmendmentRequest(VacationDecision Decision, string Reason);
public record DirectVacationCancellationRequest(string Reason);
public record SetVacationRolesRequest(IReadOnlyCollection<VacationRole> Roles);

public record VacationRequestQuery(
    VacationRequestStatus? Status = null,
    VacationRole? Stage = null,
    int? RiderId = null,
    long? EmployeeIqamaNo = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1,
    int PageSize = 50);

public record VacationRiderResponse(int RiderId, long IqamaNo, string NameAR, string NameEN, string? WorkingId, int? HousingId, string? HousingName, string? PassportNo, DateOnly? PassportEnd, DateOnly IqamaEndM, DateOnly IqamaEndH);
public record VacationEmployeeResponse(long IqamaNo, string NameAR, string NameEN, int? HousingId, string? HousingName, string? PassportNo, DateOnly? PassportEnd, DateOnly IqamaEndM, DateOnly IqamaEndH, bool IsRider);
public record VacationDecisionResponse(VacationRole Role, VacationDecision Decision, VacationRole? TargetRole, string Reason, string UserId, string UserName, DateTime DecidedAt, bool IsSuperseded, DateTime? SupersededAt);
public record VacationDateChangeResponse(Guid Id, DateOnly PreviousStartDate, DateOnly PreviousEndDate, DateOnly ProposedStartDate, DateOnly ProposedEndDate, string Reason, string RequestedByUserId, string RequestedByName, DateTime RequestedAt, VacationAmendmentStatus Status, string? ResolvedByUserId, string? ResolvedByName, string? ResolutionReason, DateTime? ResolvedAt);
public record VacationCancellationResponse(Guid Id, string Reason, string RequestedByUserId, string RequestedByName, DateTime RequestedAt, VacationAmendmentStatus Status, string? ResolvedByUserId, string? ResolvedByName, string? ResolutionReason, DateTime? ResolvedAt);
public record VacationHrDocumentResponse(Guid Id, VacationHrDocumentType Type, int Version, string FileName, string ContentType, long FileSize, string UploadedByUserId, string UploadedByName, DateTime UploadedAt, bool IsCompleted, DateTime? CompletedAt, bool IsSuperseded, DateTime? SupersededAt, string? SupersededReason, string StreamUrl, string DownloadUrl);
public record VacationHrResponse(VacationHrStatus Status, bool TicketCompleted, bool ExitReentryVisaCompleted, IReadOnlyCollection<VacationHrDocumentResponse> Documents);
public record VacationRequestResponse(Guid Id, VacationEmployeeResponse Employee, VacationRiderResponse? Rider, DateOnly StartDate, DateOnly EndDate, string? MemberNotes, VacationRequestStatus Status, VacationRole? CurrentRole, IReadOnlyCollection<VacationRole> AvailableReturnRoles, string RequestedByUserId, string RequestedByName, DateTime RequestedAt, DateTime? FullyApprovedAt, DateTime? ActivatedAt, DateTime? CompletedAt, DateTime? CancelledAt, string? CancelledByUserId, string? CancelledByName, string? CancellationReason, IReadOnlyCollection<VacationDecisionResponse> Decisions, IReadOnlyCollection<VacationDateChangeResponse> DateChanges, IReadOnlyCollection<VacationCancellationResponse> Cancellations, VacationHrResponse Hr);
public record VacationPagedResponse(IReadOnlyCollection<VacationRequestResponse> Items, int TotalCount, int Page, int PageSize);
public record VacationRoleAssignmentResponse(string UserId, string UserName, IReadOnlyCollection<VacationRole> Roles);
public record VacationHrUploadResponse(VacationRequestResponse Vacation, VacationHrDocumentResponse Document);
public record VacationDocumentFileResponse(Stream Content, string ContentType, string FileName, long Length);

public class CreateVacationRequestValidator : AbstractValidator<CreateVacationRequest>
{
    public CreateVacationRequestValidator()
    {
        RuleFor(x => x).Must(x => x.RiderId.HasValue ^ x.EmployeeIqamaNo.HasValue)
            .WithMessage("Provide exactly one of riderId or employeeIqamaNo.");
        RuleFor(x => x.RiderId!.Value).GreaterThan(0).When(x => x.RiderId.HasValue);
        RuleFor(x => x.EmployeeIqamaNo!.Value).GreaterThan(0).When(x => x.EmployeeIqamaNo.HasValue);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.MemberNotes).MaximumLength(1000);
    }
}

public class VacationDecisionRequestValidator : AbstractValidator<VacationDecisionRequest>
{
    public VacationDecisionRequestValidator()
    {
        RuleFor(x => x.Decision).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.TargetRole).NotNull().When(x => x.Decision == VacationDecision.Returned);
        RuleFor(x => x.TargetRole).Null().When(x => x.Decision != VacationDecision.Returned);
        RuleFor(x => x.TargetRole!.Value).IsInEnum().When(x => x.TargetRole.HasValue);
    }
}

public class CreateVacationDateChangeRequestValidator : AbstractValidator<CreateVacationDateChangeRequest>
{
    public CreateVacationDateChangeRequestValidator()
    {
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class CreateVacationCancellationRequestValidator : AbstractValidator<CreateVacationCancellationRequest>
{
    public CreateVacationCancellationRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class ResolveVacationAmendmentRequestValidator : AbstractValidator<ResolveVacationAmendmentRequest>
{
    public ResolveVacationAmendmentRequestValidator()
    {
        RuleFor(x => x.Decision).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class DirectVacationCancellationRequestValidator : AbstractValidator<DirectVacationCancellationRequest>
{
    public DirectVacationCancellationRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}
