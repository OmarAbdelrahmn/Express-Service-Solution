using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Vacation;
using Domain;
using Domain.Entities;
using Domain.Entities.Vacation;
using Application.Service.Empolyee;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Vacation;

public class VacationService(ApplicationDbcontext dbcontext, IVacationDocumentStorage? documentStorage = null) : IVacationService
{
    private static readonly VacationRequestStatus[] OverlapStatuses =
    [
        VacationRequestStatus.PendingOperation,
        VacationRequestStatus.PendingAccountant,
        VacationRequestStatus.PendingAdministration,
        VacationRequestStatus.Approved,
        VacationRequestStatus.Active
    ];

    public async Task<Result<VacationRequestResponse>> CreateForMemberAsync(string actorUserId, long managerIqamaNo, CreateVacationRequest request, CancellationToken cancellationToken = default)
    {
        var today = RiyadhToday();
        if (request.StartDate < today || request.EndDate < request.StartDate)
            return Result.Failure<VacationRequestResponse>(new Error("Vacation.InvalidDates", "Start date must be today or later and end date must be on or after start date.", 400));

        var housingId = await GetManagedHousingIdAsync(managerIqamaNo, cancellationToken);
        if (housingId is null)
            return Result.Failure<VacationRequestResponse>(VacationErrors.AccessDenied);

        var rider = await dbcontext.RiderDetails
            .Include(x => x.Employee).ThenInclude(x => x.Housing)
            .SingleOrDefaultAsync(x => x.Id == request.RiderId && x.Employee.HousingId == housingId && !x.Employee.IsDeleted, cancellationToken);
        if (rider is null)
            return Result.Failure<VacationRequestResponse>(VacationErrors.RiderNotFound);

        if (await HasOverlapAsync(rider.Id, request.StartDate, request.EndDate, null, cancellationToken))
            return Result.Failure<VacationRequestResponse>(VacationErrors.Overlap);

        var actorName = await GetUserNameAsync(actorUserId, cancellationToken);
        var vacation = new VacationRequest
        {
            RiderId = rider.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            MemberNotes = string.IsNullOrWhiteSpace(request.MemberNotes) ? null : request.MemberNotes.Trim(),
            RequestedByUserId = actorUserId,
            RequestedByName = actorName,
            RequestedAt = RiyadhNow(),
            Status = VacationRequestStatus.PendingOperation,
            Rider = rider
        };

        dbcontext.VacationRequests.Add(vacation);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(vacation));
    }

    public async Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetMemberRequestsAsync(long managerIqamaNo, CancellationToken cancellationToken = default)
    {
        var housingId = await GetManagedHousingIdAsync(managerIqamaNo, cancellationToken);
        if (housingId is null)
            return Result.Failure<IReadOnlyCollection<VacationRequestResponse>>(VacationErrors.AccessDenied);

        var items = await RequestsQuery()
            .Where(x => x.Rider.Employee.HousingId == housingId)
            .OrderByDescending(x => x.RequestedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationRequestResponse>>(items.Select(ToResponse).ToList());
    }

    public async Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetMemberVacationRidersAsync(long managerIqamaNo, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        var housingId = await GetManagedHousingIdAsync(managerIqamaNo, cancellationToken);
        if (housingId is null)
            return Result.Failure<IReadOnlyCollection<VacationRequestResponse>>(VacationErrors.AccessDenied);

        var from = fromDate ?? RiyadhToday();
        var to = toDate ?? from;
        if (to < from)
            return Result.Failure<IReadOnlyCollection<VacationRequestResponse>>(new Error("Vacation.InvalidDates", "End date must be on or after start date.", 400));

        var items = await RequestsQuery()
            .Where(x => x.Rider.Employee.HousingId == housingId &&
                        (x.Status == VacationRequestStatus.Approved || x.Status == VacationRequestStatus.Active) &&
                        x.StartDate <= to && x.EndDate >= from)
            .OrderBy(x => x.StartDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationRequestResponse>>(items.Select(ToResponse).ToList());
    }

    public async Task<Result<VacationDateChangeResponse>> RequestDateChangeAsync(string actorUserId, long managerIqamaNo, Guid vacationRequestId, CreateVacationDateChangeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<VacationDateChangeResponse>(RequiredReasonError());
        var vacationResult = await GetMemberVacationForUpdateAsync(managerIqamaNo, vacationRequestId, cancellationToken);
        if (vacationResult.IsFailure)
            return Result.Failure<VacationDateChangeResponse>(vacationResult.Error);
        var vacation = vacationResult.Value;

        if (!CanAmend(vacation.Status))
            return Result.Failure<VacationDateChangeResponse>(VacationErrors.InvalidState);
        if (request.EndDate < request.StartDate)
            return Result.Failure<VacationDateChangeResponse>(new Error("Vacation.InvalidDates", "End date must be on or after start date.", 400));
        if (vacation.Status != VacationRequestStatus.Active && request.StartDate < RiyadhToday())
            return Result.Failure<VacationDateChangeResponse>(new Error("Vacation.InvalidDates", "The revised start date must be today or later for a non-active vacation.", 400));
        if (await HasPendingAmendmentAsync(vacation.Id, cancellationToken))
            return Result.Failure<VacationDateChangeResponse>(VacationErrors.WorkflowPaused);
        if (await HasOverlapAsync(vacation.RiderId, request.StartDate, request.EndDate, vacation.Id, cancellationToken))
            return Result.Failure<VacationDateChangeResponse>(VacationErrors.Overlap);

        var amendment = new VacationDateChangeRequest
        {
            VacationRequestId = vacation.Id,
            PreviousStartDate = vacation.StartDate,
            PreviousEndDate = vacation.EndDate,
            ProposedStartDate = request.StartDate,
            ProposedEndDate = request.EndDate,
            Reason = request.Reason.Trim(),
            RequestedByUserId = actorUserId,
            RequestedByName = await GetUserNameAsync(actorUserId, cancellationToken),
            RequestedAt = RiyadhNow()
        };
        dbcontext.VacationDateChangeRequests.Add(amendment);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(amendment));
    }

    public async Task<Result<VacationCancellationResponse>> RequestCancellationAsync(string actorUserId, long managerIqamaNo, Guid vacationRequestId, CreateVacationCancellationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<VacationCancellationResponse>(RequiredReasonError());
        var vacationResult = await GetMemberVacationForUpdateAsync(managerIqamaNo, vacationRequestId, cancellationToken);
        if (vacationResult.IsFailure)
            return Result.Failure<VacationCancellationResponse>(vacationResult.Error);
        var vacation = vacationResult.Value;

        if (!CanCancel(vacation.Status))
            return Result.Failure<VacationCancellationResponse>(VacationErrors.InvalidState);
        if (await HasPendingAmendmentAsync(vacation.Id, cancellationToken))
            return Result.Failure<VacationCancellationResponse>(VacationErrors.WorkflowPaused);

        var cancellation = new VacationCancellationRequest
        {
            VacationRequestId = vacation.Id,
            Reason = request.Reason.Trim(),
            RequestedByUserId = actorUserId,
            RequestedByName = await GetUserNameAsync(actorUserId, cancellationToken),
            RequestedAt = RiyadhNow()
        };
        dbcontext.VacationCancellationRequests.Add(cancellation);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(cancellation));
    }

    public async Task<Result<VacationPagedResponse>> GetAllAsync(VacationRequestQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var source = RequestsQuery().AsNoTracking();
        if (query.Status.HasValue)
            source = source.Where(x => x.Status == query.Status.Value);
        if (query.Stage.HasValue)
        {
            if (!Enum.IsDefined(query.Stage.Value))
                return Result.Failure<VacationPagedResponse>(new Error("Vacation.InvalidRole", "Vacation stage is invalid.", 400));
            if (query.Stage.Value == VacationRole.HR)
                source = source.Where(x => x.FullyApprovedAt != null &&
                                           (x.HrStatus == VacationHrStatus.AwaitingTicket ||
                                            x.HrStatus == VacationHrStatus.AwaitingExitReentryVisa));
            else
            {
                var expected = StatusForRole(query.Stage.Value);
                source = source.Where(x => x.Status == expected);
            }
        }
        if (query.RiderId.HasValue)
            source = source.Where(x => x.RiderId == query.RiderId.Value);
        if (query.FromDate.HasValue)
            source = source.Where(x => x.EndDate >= query.FromDate.Value);
        if (query.ToDate.HasValue)
            source = source.Where(x => x.StartDate <= query.ToDate.Value);

        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.RequestedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Result.Success(new VacationPagedResponse(items.Select(ToResponse).ToList(), total, page, pageSize));
    }

    public async Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetInboxAsync(string actorUserId, CancellationToken cancellationToken = default)
    {
        var roles = await dbcontext.VacationUserRoleAssignments.AsNoTracking()
            .Where(x => x.UserId == actorUserId).Select(x => x.Role).ToListAsync(cancellationToken);
        var statuses = roles
            .Where(x => x is VacationRole.Operation or VacationRole.Accountant or VacationRole.Administration)
            .Select(StatusForRole)
            .ToList();
        if (statuses.Count == 0)
            return Result.Success<IReadOnlyCollection<VacationRequestResponse>>([]);

        var items = await RequestsQuery().AsNoTracking()
            .Where(x => statuses.Contains(x.Status) &&
                        !x.DateChangeRequests.Any(a => a.Status == VacationAmendmentStatus.Pending) &&
                        !x.CancellationRequests.Any(a => a.Status == VacationAmendmentStatus.Pending))
            .OrderBy(x => x.RequestedAt)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationRequestResponse>>(items.Select(ToResponse).ToList());
    }

    public async Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetHrInboxAsync(string actorUserId, CancellationToken cancellationToken = default)
    {
        if (!await IsAssignedAsync(actorUserId, VacationRole.HR, cancellationToken))
            return Result.Failure<IReadOnlyCollection<VacationRequestResponse>>(VacationErrors.AccessDenied);

        var items = await RequestsQuery().AsNoTracking()
            .Where(x => x.FullyApprovedAt != null &&
                        (x.HrStatus == VacationHrStatus.AwaitingTicket ||
                         x.HrStatus == VacationHrStatus.AwaitingExitReentryVisa) &&
                        !x.DateChangeRequests.Any(a => a.Status == VacationAmendmentStatus.Pending) &&
                        !x.CancellationRequests.Any(a => a.Status == VacationAmendmentStatus.Pending))
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.RequestedAt)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationRequestResponse>>(items.Select(ToResponse).ToList());
    }

    public async Task<Result<VacationRequestResponse>> GetDetailAsync(string actorUserId, bool isOversightUser, Guid id, CancellationToken cancellationToken = default)
    {
        var vacation = await RequestsQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (vacation is null)
            return Result.Failure<VacationRequestResponse>(VacationErrors.NotFound);
        if (!isOversightUser)
        {
            var role = RoleForStatus(vacation.Status);
            var allowed = (role.HasValue && await IsAssignedAsync(actorUserId, role.Value, cancellationToken)) ||
                          (vacation.FullyApprovedAt.HasValue && await IsAssignedAsync(actorUserId, VacationRole.HR, cancellationToken));
            if (!allowed)
                return Result.Failure<VacationRequestResponse>(VacationErrors.AccessDenied);
        }
        return Result.Success(ToResponse(vacation));
    }

    public async Task<Result<VacationRequestResponse>> DecideAsync(string actorUserId, Guid id, VacationDecisionRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Decision) || string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<VacationRequestResponse>(RequiredReasonError());
        await using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vacation = await LoadVacationForUpdateAsync(id, cancellationToken);
            if (vacation is null)
                return Result.Failure<VacationRequestResponse>(VacationErrors.NotFound);
            var role = RoleForStatus(vacation.Status);
            if (!role.HasValue)
                return Result.Failure<VacationRequestResponse>(VacationErrors.InvalidState);
            if (await HasPendingAmendmentAsync(vacation.Id, cancellationToken))
                return Result.Failure<VacationRequestResponse>(VacationErrors.WorkflowPaused);
            if (!await IsAssignedAsync(actorUserId, role.Value, cancellationToken))
                return Result.Failure<VacationRequestResponse>(VacationErrors.AccessDenied);

            var actorName = await GetUserNameAsync(actorUserId, cancellationToken);
            dbcontext.VacationApprovalDecisions.Add(new VacationApprovalDecision
            {
                VacationRequestId = vacation.Id,
                Role = role.Value,
                Decision = request.Decision,
                Reason = request.Reason.Trim(),
                DecidedByUserId = actorUserId,
                DecidedByName = actorName,
                DecidedAt = RiyadhNow()
            });

            if (request.Decision == VacationDecision.Rejected)
            {
                vacation.Status = VacationRequestStatus.Rejected;
                vacation.HrStatus = VacationHrStatus.Closed;
            }
            else if (role == VacationRole.Operation)
            {
                vacation.Status = VacationRequestStatus.PendingAccountant;
            }
            else if (role == VacationRole.Accountant)
            {
                vacation.Status = VacationRequestStatus.PendingAdministration;
            }
            else
            {
                vacation.FullyApprovedAt = RiyadhNow();
                if (RiyadhToday() > vacation.EndDate)
                {
                    vacation.Status = VacationRequestStatus.Expired;
                    vacation.HrStatus = VacationHrStatus.Closed;
                }
                else
                {
                    vacation.HrStatus = VacationHrStatus.AwaitingTicket;
                    vacation.Status = VacationRequestStatus.Approved;
                    await ApplyEffectiveDatesAsync(vacation, actorUserId, actorName, cancellationToken);
                }
            }

            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(vacation));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<VacationRequestResponse>(VacationErrors.ConcurrentUpdate);
        }
    }

    public async Task<Result<IReadOnlyCollection<VacationDateChangeResponse>>> GetDateChangesAsync(CancellationToken cancellationToken = default)
    {
        var items = await dbcontext.VacationDateChangeRequests.AsNoTracking().OrderByDescending(x => x.RequestedAt).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationDateChangeResponse>>(items.Select(ToResponse).ToList());
    }

    public async Task<Result<VacationDateChangeResponse>> ResolveDateChangeAsync(string actorUserId, Guid id, ResolveVacationAmendmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Decision) || string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<VacationDateChangeResponse>(RequiredReasonError());
        await using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var amendment = await dbcontext.VacationDateChangeRequests
                .Include(x => x.VacationRequest).ThenInclude(x => x.Rider).ThenInclude(x => x.Employee)
                .Include(x => x.VacationRequest.HrDocuments)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (amendment is null)
                return Result.Failure<VacationDateChangeResponse>(new Error("Vacation.DateChangeNotFound", "Vacation date change request was not found.", 404));
            if (amendment.Status != VacationAmendmentStatus.Pending)
                return Result.Failure<VacationDateChangeResponse>(VacationErrors.InvalidState);

            var actorName = await GetUserNameAsync(actorUserId, cancellationToken);
            amendment.ResolvedByUserId = actorUserId;
            amendment.ResolvedByName = actorName;
            amendment.ResolutionReason = request.Reason.Trim();
            amendment.ResolvedAt = RiyadhNow();
            amendment.Status = request.Decision == VacationDecision.Approved ? VacationAmendmentStatus.Approved : VacationAmendmentStatus.Rejected;

            if (request.Decision == VacationDecision.Approved)
            {
                var vacation = amendment.VacationRequest;
                var previousEndDate = vacation.EndDate;
                if (!CanAmend(vacation.Status))
                    return Result.Failure<VacationDateChangeResponse>(VacationErrors.InvalidState);
                if (vacation.Status != VacationRequestStatus.Active && amendment.ProposedStartDate < RiyadhToday())
                    return Result.Failure<VacationDateChangeResponse>(new Error("Vacation.InvalidDates", "The revised start date must be today or later for a non-active vacation.", 400));
                if (await HasOverlapAsync(vacation.RiderId, amendment.ProposedStartDate, amendment.ProposedEndDate, vacation.Id, cancellationToken))
                    return Result.Failure<VacationDateChangeResponse>(VacationErrors.Overlap);

                vacation.StartDate = amendment.ProposedStartDate;
                vacation.EndDate = amendment.ProposedEndDate;
                if (vacation.FullyApprovedAt.HasValue && amendment.ProposedEndDate > previousEndDate)
                    InvalidateExitReentryVisa(vacation, actorUserId, request.Reason.Trim());
                if (vacation.Status is VacationRequestStatus.Approved or VacationRequestStatus.Active)
                    await ApplyEffectiveDatesAsync(vacation, actorUserId, actorName, cancellationToken);
            }

            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(amendment));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<VacationDateChangeResponse>(VacationErrors.ConcurrentUpdate);
        }
    }

    public async Task<Result<IReadOnlyCollection<VacationCancellationResponse>>> GetCancellationsAsync(CancellationToken cancellationToken = default)
    {
        var items = await dbcontext.VacationCancellationRequests.AsNoTracking().OrderByDescending(x => x.RequestedAt).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationCancellationResponse>>(items.Select(ToResponse).ToList());
    }

    public async Task<Result<VacationCancellationResponse>> ResolveCancellationAsync(string actorUserId, Guid id, ResolveVacationAmendmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Decision) || string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<VacationCancellationResponse>(RequiredReasonError());
        await using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var cancellation = await dbcontext.VacationCancellationRequests
                .Include(x => x.VacationRequest).ThenInclude(x => x.Rider).ThenInclude(x => x.Employee)
                .Include(x => x.VacationRequest.DateChangeRequests)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (cancellation is null)
                return Result.Failure<VacationCancellationResponse>(new Error("Vacation.CancellationNotFound", "Vacation cancellation request was not found.", 404));
            if (cancellation.Status != VacationAmendmentStatus.Pending)
                return Result.Failure<VacationCancellationResponse>(VacationErrors.InvalidState);

            var actorName = await GetUserNameAsync(actorUserId, cancellationToken);
            cancellation.ResolvedByUserId = actorUserId;
            cancellation.ResolvedByName = actorName;
            cancellation.ResolutionReason = request.Reason.Trim();
            cancellation.ResolvedAt = RiyadhNow();
            cancellation.Status = request.Decision == VacationDecision.Approved ? VacationAmendmentStatus.Approved : VacationAmendmentStatus.Rejected;
            if (request.Decision == VacationDecision.Approved)
            {
                if (!CanCancel(cancellation.VacationRequest.Status))
                    return Result.Failure<VacationCancellationResponse>(VacationErrors.InvalidState);
                await CancelVacationAsync(cancellation.VacationRequest, actorUserId, actorName, request.Reason.Trim(), cancellation.VacationRequest.DateChangeRequests, cancellationToken);
            }

            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(cancellation));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<VacationCancellationResponse>(VacationErrors.ConcurrentUpdate);
        }
    }

    public async Task<Result<VacationRequestResponse>> DirectCancelAsync(string actorUserId, Guid id, DirectVacationCancellationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<VacationRequestResponse>(RequiredReasonError());
        await using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vacation = await dbcontext.VacationRequests
                .Include(x => x.Rider).ThenInclude(x => x.Employee)
                .Include(x => x.DateChangeRequests)
                .Include(x => x.CancellationRequests)
                .Include(x => x.Decisions)
                .Include(x => x.HrDocuments)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (vacation is null)
                return Result.Failure<VacationRequestResponse>(VacationErrors.NotFound);
            if (!CanCancel(vacation.Status))
                return Result.Failure<VacationRequestResponse>(VacationErrors.InvalidState);

            var actorName = await GetUserNameAsync(actorUserId, cancellationToken);
            await CancelVacationAsync(vacation, actorUserId, actorName, request.Reason.Trim(), vacation.DateChangeRequests, cancellationToken);
            foreach (var pendingCancellation in vacation.CancellationRequests.Where(x => x.Status == VacationAmendmentStatus.Pending))
            {
                pendingCancellation.Status = VacationAmendmentStatus.Approved;
                pendingCancellation.ResolvedByUserId = actorUserId;
                pendingCancellation.ResolvedByName = actorName;
                pendingCancellation.ResolutionReason = request.Reason.Trim();
                pendingCancellation.ResolvedAt = RiyadhNow();
            }
            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(vacation));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<VacationRequestResponse>(VacationErrors.ConcurrentUpdate);
        }
    }

    public async Task<Result<VacationHrUploadResponse>> UploadHrDocumentAsync(
        string actorUserId,
        Guid id,
        VacationHrDocumentType type,
        bool completed,
        string fileName,
        string contentType,
        long fileSize,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAssignedAsync(actorUserId, VacationRole.HR, cancellationToken))
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.AccessDenied);
        if (!Enum.IsDefined(type) || fileSize <= 0 || fileSize > VacationDocumentStorage.MaximumFileSize || documentStorage is null)
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.InvalidDocument);
        var safeFileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 260)
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.InvalidDocument);

        var vacation = await LoadVacationForUpdateAsync(id, cancellationToken);
        if (vacation is null)
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.NotFound);
        if (!vacation.FullyApprovedAt.HasValue ||
            vacation.Status is VacationRequestStatus.Rejected or VacationRequestStatus.Cancelled or VacationRequestStatus.Expired ||
            vacation.HrStatus == VacationHrStatus.Closed)
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.HrNotReady);
        if (await HasPendingAmendmentAsync(vacation.Id, cancellationToken))
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.WorkflowPaused);

        var ticketCompleted = HasCurrentCompletedDocument(vacation, VacationHrDocumentType.Ticket);
        if (type == VacationHrDocumentType.ExitReentryVisa && !ticketCompleted)
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.TicketRequired);

        var actorName = await GetUserNameAsync(actorUserId, cancellationToken);
        var now = RiyadhNow();
        var previous = vacation.HrDocuments
            .Where(x => x.Type == type && !x.IsSuperseded)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();
        var document = new VacationHrDocument
        {
            VacationRequestId = vacation.Id,
            Type = type,
            Version = vacation.HrDocuments.Where(x => x.Type == type).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1,
            OriginalFileName = safeFileName,
            UploadedByUserId = actorUserId,
            UploadedByName = actorName,
            UploadedAt = now,
            IsCompleted = completed,
            CompletedAt = completed ? now : null
        };

        StoredVacationDocument stored;
        try
        {
            stored = await documentStorage.SaveAsync(
                vacation.Id,
                document.Id,
                type == VacationHrDocumentType.Ticket ? "ticket" : "exit-reentry-visa",
                safeFileName,
                content,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.InvalidDocument);
        }

        document.StoredRelativePath = stored.RelativePath;
        document.ContentType = stored.ContentType;
        document.FileSize = stored.Length;
        if (previous is not null)
        {
            previous.IsSuperseded = true;
            previous.SupersededAt = now;
            previous.SupersededByUserId = actorUserId;
            previous.SupersededReason = "Replaced by a newer HR document.";
        }
        vacation.HrDocuments.Add(document);
        dbcontext.VacationHrDocuments.Add(document);
        RefreshHrStatus(vacation);

        try
        {
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await documentStorage.DeleteAsync(stored.RelativePath, cancellationToken);
            return Result.Failure<VacationHrUploadResponse>(VacationErrors.ConcurrentUpdate);
        }
        catch
        {
            await documentStorage.DeleteAsync(stored.RelativePath, CancellationToken.None);
            throw;
        }

        return Result.Success(new VacationHrUploadResponse(ToResponse(vacation), ToResponse(document)));
    }

    public async Task<Result<VacationDocumentFileResponse>> OpenHrDocumentAsync(
        string actorUserId,
        long memberIqamaNo,
        bool isOversightUser,
        Guid vacationRequestId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbcontext.VacationHrDocuments.AsNoTracking()
            .Include(x => x.VacationRequest).ThenInclude(x => x.Rider).ThenInclude(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == documentId && x.VacationRequestId == vacationRequestId, cancellationToken);
        if (document is null)
            return Result.Failure<VacationDocumentFileResponse>(VacationErrors.DocumentNotFound);

        var allowed = isOversightUser || await IsAssignedAsync(actorUserId, VacationRole.HR, cancellationToken);
        if (!allowed && memberIqamaNo != 0)
        {
            var housingId = await GetManagedHousingIdAsync(memberIqamaNo, cancellationToken);
            allowed = housingId.HasValue && document.VacationRequest.Rider.Employee.HousingId == housingId.Value;
        }
        if (!allowed)
            return Result.Failure<VacationDocumentFileResponse>(VacationErrors.AccessDenied);
        if (documentStorage is null)
            return Result.Failure<VacationDocumentFileResponse>(VacationErrors.DocumentNotFound);

        var stream = await documentStorage.OpenReadAsync(document.StoredRelativePath, cancellationToken);
        return stream is null
            ? Result.Failure<VacationDocumentFileResponse>(VacationErrors.DocumentNotFound)
            : Result.Success(new VacationDocumentFileResponse(stream, document.ContentType, document.OriginalFileName, document.FileSize));
    }

    public async Task<Result<IReadOnlyCollection<VacationRoleAssignmentResponse>>> GetRoleAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        var assignments = await dbcontext.VacationUserRoleAssignments.AsNoTracking()
            .Include(x => x.User).OrderBy(x => x.User.UserName).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<VacationRoleAssignmentResponse>>(assignments
            .GroupBy(x => new { x.UserId, UserName = x.User.UserName ?? x.User.FullName ?? x.UserId })
            .Select(x => new VacationRoleAssignmentResponse(x.Key.UserId, x.Key.UserName, x.Select(a => a.Role).OrderBy(a => a).ToList()))
            .ToList());
    }

    public async Task<Result<IReadOnlyCollection<VacationRoleAssignmentResponse>>> SetRolesAsync(string grantedByUserId, string userId, SetVacationRolesRequest request, CancellationToken cancellationToken = default)
    {
        if (!await dbcontext.ApplicationUsers.AnyAsync(x => x.Id == userId, cancellationToken))
            return Result.Failure<IReadOnlyCollection<VacationRoleAssignmentResponse>>(UserErrors.UserNotFound);
        var roles = (request.Roles ?? []).Distinct().ToList();
        if (roles.Any(x => !Enum.IsDefined(x)))
            return Result.Failure<IReadOnlyCollection<VacationRoleAssignmentResponse>>(new Error("Vacation.InvalidRole", "One or more vacation roles are invalid.", 400));

        var current = await dbcontext.VacationUserRoleAssignments.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        dbcontext.VacationUserRoleAssignments.RemoveRange(current);
        foreach (var role in roles)
            dbcontext.VacationUserRoleAssignments.Add(new VacationUserRoleAssignment { UserId = userId, Role = role, GrantedBy = grantedByUserId, GrantedAt = RiyadhNow() });
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await GetRoleAssignmentsAsync(cancellationToken);
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var today = RiyadhToday();
        var requests = await dbcontext.VacationRequests
            .Include(x => x.Rider).ThenInclude(x => x.Employee)
            .Where(x => x.Status == VacationRequestStatus.Approved || x.Status == VacationRequestStatus.Active ||
                        ((x.Status == VacationRequestStatus.PendingOperation || x.Status == VacationRequestStatus.PendingAccountant || x.Status == VacationRequestStatus.PendingAdministration) && x.EndDate < today))
            .ToListAsync(cancellationToken);

        foreach (var vacation in requests)
        {
            if (vacation.Status is VacationRequestStatus.PendingOperation or VacationRequestStatus.PendingAccountant or VacationRequestStatus.PendingAdministration)
            {
                vacation.Status = VacationRequestStatus.Expired;
                vacation.HrStatus = VacationHrStatus.Closed;
                continue;
            }
            await ApplyEffectiveDatesAsync(vacation, "VacationWorkflow", "Vacation Workflow", cancellationToken);
        }
        if (requests.Count > 0)
            await dbcontext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<VacationRequest> RequestsQuery() => dbcontext.VacationRequests
        .Include(x => x.Rider).ThenInclude(x => x.Employee).ThenInclude(x => x.Housing)
        .Include(x => x.Decisions)
        .Include(x => x.DateChangeRequests)
        .Include(x => x.CancellationRequests)
        .Include(x => x.HrDocuments)
        .AsSplitQuery();

    private async Task<VacationRequest?> LoadVacationForUpdateAsync(Guid id, CancellationToken cancellationToken) => await dbcontext.VacationRequests
        .Include(x => x.Rider).ThenInclude(x => x.Employee)
        .Include(x => x.Decisions)
        .Include(x => x.DateChangeRequests)
        .Include(x => x.CancellationRequests)
        .Include(x => x.HrDocuments)
        .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task<Result<VacationRequest>> GetMemberVacationForUpdateAsync(long managerIqamaNo, Guid id, CancellationToken cancellationToken)
    {
        var housingId = await GetManagedHousingIdAsync(managerIqamaNo, cancellationToken);
        if (housingId is null)
            return Result.Failure<VacationRequest>(VacationErrors.AccessDenied);
        var vacation = await dbcontext.VacationRequests
            .Include(x => x.Rider).ThenInclude(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id && x.Rider.Employee.HousingId == housingId, cancellationToken);
        return vacation is null ? Result.Failure<VacationRequest>(VacationErrors.NotFound) : Result.Success(vacation);
    }

    private async Task<int?> GetManagedHousingIdAsync(long managerIqamaNo, CancellationToken cancellationToken) => await dbcontext.Housings
        .Where(x => x.ManagerIqamaNo == managerIqamaNo).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> HasOverlapAsync(int riderId, DateOnly startDate, DateOnly endDate, Guid? excludedRequestId, CancellationToken cancellationToken) => await dbcontext.VacationRequests
        .AnyAsync(x => x.RiderId == riderId && OverlapStatuses.Contains(x.Status) && (!excludedRequestId.HasValue || x.Id != excludedRequestId.Value) && x.StartDate <= endDate && x.EndDate >= startDate, cancellationToken);

    private async Task<bool> HasPendingAmendmentAsync(Guid vacationRequestId, CancellationToken cancellationToken) =>
        await dbcontext.VacationDateChangeRequests.AnyAsync(x => x.VacationRequestId == vacationRequestId && x.Status == VacationAmendmentStatus.Pending, cancellationToken) ||
        await dbcontext.VacationCancellationRequests.AnyAsync(x => x.VacationRequestId == vacationRequestId && x.Status == VacationAmendmentStatus.Pending, cancellationToken);

    private Task<bool> IsAssignedAsync(string actorUserId, VacationRole role, CancellationToken cancellationToken) => dbcontext.VacationUserRoleAssignments
        .AnyAsync(x => x.UserId == actorUserId && x.Role == role, cancellationToken);

    private async Task<string> GetUserNameAsync(string userId, CancellationToken cancellationToken) => await dbcontext.ApplicationUsers
        .Where(x => x.Id == userId).Select(x => x.FullName ?? x.UserName ?? x.Id).SingleOrDefaultAsync(cancellationToken) ?? userId;

    private async Task ApplyEffectiveDatesAsync(VacationRequest vacation, string actorUserId, string actorName, CancellationToken cancellationToken)
    {
        var today = RiyadhToday();
        if (today > vacation.EndDate)
        {
            var wasActive = vacation.Status == VacationRequestStatus.Active;
            vacation.Status = VacationRequestStatus.Completed;
            vacation.CompletedAt ??= RiyadhNow();
            if (wasActive)
                await SetEmployeeStatusAsync(vacation, "disable", actorName, "Vacation completed", cancellationToken);
            return;
        }
        if (today >= vacation.StartDate)
        {
            vacation.Status = VacationRequestStatus.Active;
            vacation.ActivatedAt ??= RiyadhNow();
            await SetEmployeeStatusAsync(vacation, EmployeeStatus.Vacation, actorName, "Vacation activated", cancellationToken);
            return;
        }

        var wasActiveBeforeReschedule = vacation.Status == VacationRequestStatus.Active;
        vacation.Status = VacationRequestStatus.Approved;
        if (wasActiveBeforeReschedule)
            await SetEmployeeStatusAsync(vacation, "disable", actorName, "Vacation rescheduled", cancellationToken);
    }

    private async Task CancelVacationAsync(VacationRequest vacation, string actorUserId, string actorName, string reason, IEnumerable<VacationDateChangeRequest> amendments, CancellationToken cancellationToken)
    {
        if (!CanCancel(vacation.Status))
            throw new InvalidOperationException("Vacation request cannot be cancelled in its current state.");
        var wasActive = vacation.Status == VacationRequestStatus.Active;
        vacation.Status = VacationRequestStatus.Cancelled;
        vacation.HrStatus = VacationHrStatus.Closed;
        vacation.CancelledAt = RiyadhNow();
        vacation.CancelledByUserId = actorUserId;
        vacation.CancelledByName = actorName;
        vacation.CancellationReason = reason;
        foreach (var pendingAmendment in amendments.Where(x => x.Status == VacationAmendmentStatus.Pending))
        {
            pendingAmendment.Status = VacationAmendmentStatus.Superseded;
            pendingAmendment.ResolvedByUserId = actorUserId;
            pendingAmendment.ResolvedByName = actorName;
            pendingAmendment.ResolutionReason = "Superseded by vacation cancellation.";
            pendingAmendment.ResolvedAt = RiyadhNow();
        }
        if (wasActive)
            await SetEmployeeStatusAsync(vacation, "disable", actorName, "Vacation cancelled: " + reason, cancellationToken);
    }

    private Task SetEmployeeStatusAsync(VacationRequest vacation, string status, string changedBy, string reason, CancellationToken cancellationToken)
    {
        var employee = vacation.Rider.Employee;
        if (string.Equals(employee.Status, status, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        var oldStatus = employee.Status;
        employee.Status = status;
        employee.UpdatedAt = RiyadhNow();
        dbcontext.EmployeeStatusLogs.Add(new EmployeeStatusLog
        {
            EmployeeIqamaNo = employee.IqamaNo,
            OldStatus = oldStatus,
            NewStatus = status,
            ChangedBy = changedBy,
            ChangedAt = RiyadhNow(),
            Reason = reason,
            ChangeSource = "VacationRequest"
        });
        return Task.CompletedTask;
    }

    private static bool CanAmend(VacationRequestStatus status) => status is VacationRequestStatus.PendingOperation or VacationRequestStatus.PendingAccountant or VacationRequestStatus.PendingAdministration or VacationRequestStatus.Approved or VacationRequestStatus.Active;
    private static bool CanCancel(VacationRequestStatus status) => CanAmend(status);
    private static VacationRequestStatus StatusForRole(VacationRole role) => role switch
    {
        VacationRole.Operation => VacationRequestStatus.PendingOperation,
        VacationRole.Accountant => VacationRequestStatus.PendingAccountant,
        VacationRole.Administration => VacationRequestStatus.PendingAdministration,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
    private static VacationRole? RoleForStatus(VacationRequestStatus status) => status switch
    {
        VacationRequestStatus.PendingOperation => VacationRole.Operation,
        VacationRequestStatus.PendingAccountant => VacationRole.Accountant,
        VacationRequestStatus.PendingAdministration => VacationRole.Administration,
        _ => null
    };

    private static bool HasCurrentCompletedDocument(VacationRequest vacation, VacationHrDocumentType type) =>
        vacation.HrDocuments.Any(x => x.Type == type && !x.IsSuperseded && x.IsCompleted);

    private static void RefreshHrStatus(VacationRequest vacation)
    {
        if (vacation.Status is VacationRequestStatus.Rejected or VacationRequestStatus.Cancelled or VacationRequestStatus.Expired)
        {
            vacation.HrStatus = VacationHrStatus.Closed;
            return;
        }

        if (!vacation.FullyApprovedAt.HasValue)
        {
            vacation.HrStatus = VacationHrStatus.PendingApproval;
            return;
        }

        var ticketCompleted = HasCurrentCompletedDocument(vacation, VacationHrDocumentType.Ticket);
        var visaCompleted = HasCurrentCompletedDocument(vacation, VacationHrDocumentType.ExitReentryVisa);
        vacation.HrStatus = !ticketCompleted
            ? VacationHrStatus.AwaitingTicket
            : !visaCompleted
                ? VacationHrStatus.AwaitingExitReentryVisa
                : VacationHrStatus.Completed;
    }

    private static void InvalidateExitReentryVisa(VacationRequest vacation, string actorUserId, string masterReason)
    {
        var now = RiyadhNow();
        foreach (var visa in vacation.HrDocuments.Where(x => x.Type == VacationHrDocumentType.ExitReentryVisa && !x.IsSuperseded))
        {
            visa.IsSuperseded = true;
            visa.SupersededAt = now;
            visa.SupersededByUserId = actorUserId;
            visa.SupersededReason = "Return date was extended. A new exit/re-entry visa is required. Master reason: " + masterReason;
        }
        RefreshHrStatus(vacation);
    }
    private static DateTime RiyadhNow() => DateTime.UtcNow.AddHours(3);
    private static DateOnly RiyadhToday() => DateOnly.FromDateTime(RiyadhNow());
    private static Error RequiredReasonError() => new("Vacation.ReasonRequired", "A decision reason is required.", 400);

    private static VacationRiderResponse ToResponse(RiderDetails rider) => new(rider.Id, rider.EmployeeIqamaNo, rider.Employee.NameAR, rider.Employee.NameEN, rider.WorkingId, rider.Employee.HousingId, rider.Employee.Housing?.Name);
    private static VacationDecisionResponse ToResponse(VacationApprovalDecision decision) => new(decision.Role, decision.Decision, decision.Reason, decision.DecidedByUserId, decision.DecidedByName, decision.DecidedAt);
    private static VacationDateChangeResponse ToResponse(VacationDateChangeRequest amendment) => new(amendment.Id, amendment.PreviousStartDate, amendment.PreviousEndDate, amendment.ProposedStartDate, amendment.ProposedEndDate, amendment.Reason, amendment.RequestedByUserId, amendment.RequestedByName, amendment.RequestedAt, amendment.Status, amendment.ResolvedByUserId, amendment.ResolvedByName, amendment.ResolutionReason, amendment.ResolvedAt);
    private static VacationCancellationResponse ToResponse(VacationCancellationRequest cancellation) => new(cancellation.Id, cancellation.Reason, cancellation.RequestedByUserId, cancellation.RequestedByName, cancellation.RequestedAt, cancellation.Status, cancellation.ResolvedByUserId, cancellation.ResolvedByName, cancellation.ResolutionReason, cancellation.ResolvedAt);
    private static VacationHrDocumentResponse ToResponse(VacationHrDocument document)
    {
        var baseUrl = $"/api/vacation-requests/{document.VacationRequestId}/documents/{document.Id}";
        return new VacationHrDocumentResponse(document.Id, document.Type, document.Version, document.OriginalFileName, document.ContentType, document.FileSize, document.UploadedByUserId, document.UploadedByName, document.UploadedAt, document.IsCompleted, document.CompletedAt, document.IsSuperseded, document.SupersededAt, document.SupersededReason, baseUrl + "/stream", baseUrl + "/download");
    }
    private static VacationRequestResponse ToResponse(VacationRequest vacation)
    {
        var documents = vacation.HrDocuments.OrderByDescending(x => x.UploadedAt).Select(ToResponse).ToList();
        var hr = new VacationHrResponse(
            vacation.HrStatus,
            HasCurrentCompletedDocument(vacation, VacationHrDocumentType.Ticket),
            HasCurrentCompletedDocument(vacation, VacationHrDocumentType.ExitReentryVisa),
            documents);
        return new VacationRequestResponse(vacation.Id, ToResponse(vacation.Rider), vacation.StartDate, vacation.EndDate, vacation.MemberNotes, vacation.Status, RoleForStatus(vacation.Status), vacation.RequestedByUserId, vacation.RequestedByName, vacation.RequestedAt, vacation.FullyApprovedAt, vacation.ActivatedAt, vacation.CompletedAt, vacation.CancelledAt, vacation.CancelledByUserId, vacation.CancelledByName, vacation.CancellationReason, vacation.Decisions.OrderBy(x => x.Role).Select(ToResponse).ToList(), vacation.DateChangeRequests.OrderByDescending(x => x.RequestedAt).Select(ToResponse).ToList(), vacation.CancellationRequests.OrderByDescending(x => x.RequestedAt).Select(ToResponse).ToList(), hr);
    }
}
