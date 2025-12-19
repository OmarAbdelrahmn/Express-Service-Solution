using Application.Abstraction;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Service.Riders;

public class RiderSub(
    ApplicationDbcontext dbcontext,
    IRiderWorkingIdHistoryService workingIdHistoryService) : IRiderSub
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;
    private readonly IRiderWorkingIdHistoryService _workingIdHistoryService = workingIdHistoryService;

    public async Task<Result<RiderSubstitutionResponse>> StartSubstitution(
        StartSubstitutionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Check if there's already an active substitution for this WorkingId
            var hasActiveSubstitution = await _dbcontext.RiderShiftSubstitutions
                .AnyAsync(s => s.ActualRiderWorkingId == request.ActualRiderWorkingId && s.IsActive,
                         cancellationToken);

            if (hasActiveSubstitution)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("AlreadyExists",
                             $"WorkingId {request.ActualRiderWorkingId} already has an active substitution",
                             400));

            // Get ownership information for the WorkingId
            var ownershipInfo = await _workingIdHistoryService.WhoHasWorkingId(
                request.ActualRiderWorkingId,
                cancellationToken);

            RiderDetails? actualRider = null;
            long? originalRiderIqamaNo = null;

            // Case 1: WorkingId is currently active and assigned
            if (ownershipInfo.IsSuccess && ownershipInfo.Value.IsCurrentlyAssigned)
            {
                var riderResult = await _workingIdHistoryService.GetRiderByWorkingId(
                    request.ActualRiderWorkingId,
                    cancellationToken);

                if (riderResult.IsSuccess && riderResult.Value != null)
                {
                    actualRider = riderResult.Value;
                    originalRiderIqamaNo = ownershipInfo.Value.CurrentRiderIqamaNo;
                }
            }
            // Case 2: WorkingId is old/inactive - get the last owner's rider details
            else if (ownershipInfo.IsSuccess && ownershipInfo.Value.PreviousOwners.Any())
            {
                var lastOwner = ownershipInfo.Value.PreviousOwners.First();
                originalRiderIqamaNo = lastOwner.RiderIqamaNo;

                // Fetch the actual rider details from the last owner
                actualRider = await _dbcontext.RiderDetails
                    .Include(r => r.Employee)
                    .Include(r => r.Company)
                    .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == lastOwner.RiderIqamaNo,
                                       cancellationToken);

                // If rider not found by IqamaNo, this might be a deleted rider
                // In that case, actualRider remains null but we have the IqamaNo for tracking
            }
            // Case 3: WorkingId doesn't exist in system at all
            // actualRider remains null, originalRiderIqamaNo remains null
            // This allows creating substitutions for future/temporary WorkingIds

            // Get substitute rider details
            var substituteRider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == request.SubstituteWorkingId,
                                   cancellationToken);

            if (substituteRider is null)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("NotFound", "Substitute rider not found", 404));

            // Validate that substitute is not the same as actual
            if (substituteRider.WorkingId == request.ActualRiderWorkingId)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("InvalidOperation", "Cannot substitute with same WorkingId", 400));

            // Create the substitution record
            var substitution = new RiderShiftSubstitution
            {
                ActualRiderId = actualRider?.Id,  // May be null if WorkingId doesn't exist or rider deleted
                ActualRiderWorkingId = request.ActualRiderWorkingId,
                OriginalRiderIqamaNo = originalRiderIqamaNo,  // Tracked even if rider deleted
                SubstituteRiderId = substituteRider.Id,
                SubstituteWorkingId = substituteRider.WorkingId!,
                StartDate = DateTime.UtcNow.AddHours(3),
                EndDate = null,
                Reason = request.Reason,
                CreatedBy = request.CreatedBy ?? "System",
                IsActive = true
            };

            _dbcontext.RiderShiftSubstitutions.Add(substitution);
            await _dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Build appropriate display name for the actual rider
            var actualRiderName = actualRider?.Employee?.NameEN
                ?? (originalRiderIqamaNo.HasValue
                    ? $"Former Rider [IqamaNo: {originalRiderIqamaNo}]"
                    : $"Unassigned WorkingId [{request.ActualRiderWorkingId}]");

            var response = new RiderSubstitutionResponse(
                substitution.Id,
                actualRiderName,
                substitution.ActualRiderWorkingId,
                substituteRider.Employee.NameEN,
                substitution.SubstituteWorkingId,
                substitution.StartDate,
                substitution.EndDate,
                substitution.Reason!,
                substitution.IsActive
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RiderSubstitutionResponse>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<RiderSubstitutionResponse>> StopSubstitutionByWorkingId(
        string WorkingId,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var substitution = await _dbcontext.Set<RiderShiftSubstitution>()
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.SubstituteRider)
                    .ThenInclude(r => r.Employee)
                .FirstOrDefaultAsync(s => s.ActualRiderWorkingId == WorkingId && s.IsActive,
                    cancellationToken);

            if (substitution is null)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("NotFound", "No active substitution found for this WorkingId", 404));

            substitution.EndDate = DateTime.UtcNow.AddHours(3);
            substitution.IsActive = false;

            await _dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var actualRiderName = substitution.ActualRider?.Employee?.NameEN
                ?? (substitution.OriginalRiderIqamaNo.HasValue
                    ? $"Former Rider [IqamaNo: {substitution.OriginalRiderIqamaNo}]"
                    : $"Unassigned WorkingId [{substitution.ActualRiderWorkingId}]");

            var response = new RiderSubstitutionResponse(
                substitution.Id,
                actualRiderName,
                substitution.ActualRiderWorkingId,
                substitution.SubstituteRider.Employee.NameEN,
                substitution.SubstituteWorkingId,
                substitution.StartDate,
                substitution.EndDate,
                substitution.Reason,
                substitution.IsActive
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RiderSubstitutionResponse>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetActiveSubstitutions(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.SubstituteRider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.IsActive)
                .ToListAsync(cancellationToken);

            var responses = substitutions.Select(s =>
            {
                var actualRiderName = s.ActualRider?.Employee?.NameEN
                    ?? (s.OriginalRiderIqamaNo.HasValue
                        ? $"Former Rider [IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"Unassigned WorkingId [{s.ActualRiderWorkingId}]");

                return new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                );
            });

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderSubstitutionResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetSubstitutionHistory(
        string riderWorkingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.SubstituteRider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ActualRiderWorkingId == riderWorkingId ||
                           s.SubstituteWorkingId == riderWorkingId)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync(cancellationToken);

            var responses = substitutions.Select(s =>
            {
                var actualRiderName = s.ActualRider?.Employee?.NameEN
                    ?? (s.OriginalRiderIqamaNo.HasValue
                        ? $"Former Rider [IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"Unassigned WorkingId [{s.ActualRiderWorkingId}]");

                return new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                );
            });

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderSubstitutionResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetInactiveSubstitutions(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.SubstituteRider)
                    .ThenInclude(r => r.Employee)
                .Where(s => !s.IsActive)
                .OrderByDescending(s => s.EndDate)
                .ToListAsync(cancellationToken);

            var responses = substitutions.Select(s =>
            {
                var actualRiderName = s.ActualRider?.Employee?.NameEN
                    ?? (s.OriginalRiderIqamaNo.HasValue
                        ? $"Former Rider [IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"Unassigned WorkingId [{s.ActualRiderWorkingId}]");

                return new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                );
            });

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderSubstitutionResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetAllSubstitutions(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.SubstituteRider)
                    .ThenInclude(r => r.Employee)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync(cancellationToken);

            var responses = substitutions.Select(s =>
            {
                var actualRiderName = s.ActualRider?.Employee?.NameEN
                    ?? (s.OriginalRiderIqamaNo.HasValue
                        ? $"Former Rider [IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"Unassigned WorkingId [{s.ActualRiderWorkingId}]");

                return new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                );
            });

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderSubstitutionResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }
}