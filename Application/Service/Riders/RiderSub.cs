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
            var hasActiveSubstitution = await _dbcontext.RiderShiftSubstitutions
                .AnyAsync(s => s.ActualRiderWorkingId == request.ActualRiderWorkingId && s.IsActive,
                         cancellationToken);

            if (hasActiveSubstitution)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("AlreadyExists",
                             $"WorkingId {request.ActualRiderWorkingId} already has an active substitution",
                             400));

            var ownershipInfo = await _workingIdHistoryService.WhoHasWorkingId(
                request.ActualRiderWorkingId,
                cancellationToken);

            RiderDetails? actualRider = null;
            long? originalRiderIqamaNo = null;
            string actualRiderDisplayName = $"Unassigned WorkingId [{request.ActualRiderWorkingId}]";

            if (ownershipInfo.IsSuccess && ownershipInfo.Value.IsCurrentlyAssigned)
            {
                var riderResult = await _workingIdHistoryService.GetRiderByWorkingId(
                    request.ActualRiderWorkingId,
                    cancellationToken);

                if (riderResult.IsSuccess && riderResult.Value != null)
                {
                    actualRider = riderResult.Value;
                    originalRiderIqamaNo = ownershipInfo.Value.CurrentRiderIqamaNo;
                    actualRiderDisplayName = actualRider.Employee.NameEN;
                }
            }
            // Case 2: WorkingId has history but not currently assigned
            else if (ownershipInfo.IsSuccess && ownershipInfo.Value.PreviousOwners.Any())
            {
                // Get the last owner from history
                var lastOwner = ownershipInfo.Value.PreviousOwners.First();
                originalRiderIqamaNo = lastOwner.RiderIqamaNo;

                // Use the name from history
                actualRiderDisplayName = $"{lastOwner.RiderName} [Former WorkingId: {request.ActualRiderWorkingId}]";

                // Try to fetch the actual rider details (might be null if rider was deleted)
                actualRider = await _dbcontext.RiderDetails
                    .Include(r => r.Employee)
                    .Include(r => r.Company)
                    .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == lastOwner.RiderIqamaNo,
                                       cancellationToken);
            }
            // Case 3: WorkingId doesn't exist in system at all - keep default "Unassigned" message

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
                ActualRiderId = actualRider?.Id,
                ActualRiderWorkingId = request.ActualRiderWorkingId,
                OriginalRiderIqamaNo = originalRiderIqamaNo,
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

            var response = new RiderSubstitutionResponse(
                substitution.Id,
                actualRiderDisplayName,
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

            // Build proper display name
            var actualRiderName = await BuildActualRiderDisplayName(
                substitution.ActualRider,
                substitution.OriginalRiderIqamaNo,
                substitution.ActualRiderWorkingId,
                cancellationToken);

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

            var responses = new List<RiderSubstitutionResponse>();

            foreach (var s in substitutions)
            {
                var actualRiderName = await BuildActualRiderDisplayName(
                    s.ActualRider,
                    s.OriginalRiderIqamaNo,
                    s.ActualRiderWorkingId,
                    cancellationToken);

                responses.Add(new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ));
            }

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(responses);
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

            var responses = new List<RiderSubstitutionResponse>();

            foreach (var s in substitutions)
            {
                var actualRiderName = await BuildActualRiderDisplayName(
                    s.ActualRider,
                    s.OriginalRiderIqamaNo,
                    s.ActualRiderWorkingId,
                    cancellationToken);

                responses.Add(new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ));
            }

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(responses);
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

            var responses = new List<RiderSubstitutionResponse>();

            foreach (var s in substitutions)
            {
                var actualRiderName = await BuildActualRiderDisplayName(
                    s.ActualRider,
                    s.OriginalRiderIqamaNo,
                    s.ActualRiderWorkingId,
                    cancellationToken);

                responses.Add(new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ));
            }

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(responses);
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

            var responses = new List<RiderSubstitutionResponse>();

            foreach (var s in substitutions)
            {
                var actualRiderName = await BuildActualRiderDisplayName(
                    s.ActualRider,
                    s.OriginalRiderIqamaNo,
                    s.ActualRiderWorkingId,
                    cancellationToken);

                responses.Add(new RiderSubstitutionResponse(
                    s.Id,
                    actualRiderName,
                    s.ActualRiderWorkingId,
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ));
            }

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderSubstitutionResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    /// <summary>
    /// Helper method to build proper display name for actual rider using WorkingIdHistory
    /// </summary>
    private async Task<string> BuildActualRiderDisplayName(
        RiderDetails? actualRider,
        long? originalRiderIqamaNo,
        string actualRiderWorkingId,
        CancellationToken cancellationToken)
    {
        // If we have the actual rider object, use it
        if (actualRider?.Employee != null)
        {
            return actualRider.Employee.NameEN;
        }

        // Check WorkingIdHistory for previous owner information
        var historyResult = await _workingIdHistoryService.WhoHasWorkingId(
            actualRiderWorkingId,
            cancellationToken);

        if (historyResult.IsSuccess)
        {
            // If currently assigned, get current rider name
            if (historyResult.Value.IsCurrentlyAssigned &&
                !string.IsNullOrEmpty(historyResult.Value.CurrentRiderName))
            {
                return historyResult.Value.CurrentRiderName;
            }

            // If has previous owners, use the most recent one
            if (historyResult.Value.PreviousOwners.Any())
            {
                var lastOwner = historyResult.Value.PreviousOwners.First();
                return $"{lastOwner.RiderName} [Former WorkingId: {actualRiderWorkingId}]";
            }
        }

        // Fallback: Check if we have IqamaNo stored
        if (originalRiderIqamaNo.HasValue)
        {
            // Try to get rider info from IqamaNo
            var riderByIqama = await _dbcontext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IqamaNo == originalRiderIqamaNo.Value,
                    cancellationToken);

            if (riderByIqama != null)
            {
                return $"{riderByIqama.NameEN} [IqamaNo: {originalRiderIqamaNo}]";
            }

            return $"Former Rider [IqamaNo: {originalRiderIqamaNo}]";
        }

        // Last resort fallback
        return $"Unassigned WorkingId [{actualRiderWorkingId}]";
    }
}