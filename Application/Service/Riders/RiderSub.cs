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
            // Check if already has active substitution
            var hasActiveSubstitution = await _dbcontext.RiderShiftSubstitutions
                .AnyAsync(s => s.ActualRiderWorkingId == request.ActualRiderWorkingId && s.IsActive,
                         cancellationToken);

            if (hasActiveSubstitution)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("AlreadyExists",
                             $"WorkingId {request.ActualRiderWorkingId} already has an active substitution",
                             400));



            RiderDetails? actualRider = null;
            long? originalRiderIqamaNo = null;
            string actualRiderDisplayName = $"Unassigned WorkingId [{request.ActualRiderWorkingId}]";

            // STEP 1: Check current active riders
            var currentRider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == request.ActualRiderWorkingId,
                                    cancellationToken);

            if (currentRider != null)
            {
                actualRider = currentRider;
                originalRiderIqamaNo = currentRider.EmployeeIqamaNo;
                actualRiderDisplayName = currentRider.Employee.NameEN;
            }
            else
            {
                var deletedEmployee = await _dbcontext.DeletedEmployees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.WorkingId == request.ActualRiderWorkingId,
                                        cancellationToken);

                if (deletedEmployee != null)
                {
                    originalRiderIqamaNo = deletedEmployee.IqamaNo;
                    actualRiderDisplayName = $"Deleted Employee, Former ID: {request.ActualRiderWorkingId}]";

                    // Check if this deleted employee was re-added
                    var restoredRider = await _dbcontext.RiderDetails
                        .Include(r => r.Employee)
                        .Include(r => r.Company)
                        .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == deletedEmployee.IqamaNo,
                                            cancellationToken);

                    if (restoredRider != null)
                    {
                        actualRider = restoredRider;
                        actualRiderDisplayName = $"{restoredRider.Employee.NameEN} [Former ID: {request.ActualRiderWorkingId}, Current ID: {restoredRider.WorkingId}]";
                    }
                }
                else
                {
                    var ownershipInfo = await _workingIdHistoryService.WhoHasWorkingId(
                        request.ActualRiderWorkingId,
                        cancellationToken);

                    if (ownershipInfo.IsSuccess)
                    {
                        // Currently assigned to someone
                        if (ownershipInfo.Value.IsCurrentlyAssigned)
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
                        // Has history but not currently assigned
                        else if (ownershipInfo.Value.PreviousOwners.Any())
                        {
                            var lastOwner = ownershipInfo.Value.PreviousOwners.First();
                            originalRiderIqamaNo = lastOwner.RiderIqamaNo;

                            // Try to find the rider by IqamaNo
                            actualRider = await _dbcontext.RiderDetails
                                .Include(r => r.Employee)
                                .Include(r => r.Company)
                                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == lastOwner.RiderIqamaNo,
                                                   cancellationToken);

                            if (actualRider != null)
                            {
                                actualRiderDisplayName = $"{lastOwner.RiderName} [Former ID: {request.ActualRiderWorkingId}, Current ID: {actualRider.WorkingId}]";
                            }
                            else
                            {
                                // Check if in deleted employees
                                var deletedFormer = await _dbcontext.DeletedEmployees
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(d => d.IqamaNo == lastOwner.RiderIqamaNo,
                                                        cancellationToken);

                                if (deletedFormer != null)
                                {
                                    actualRiderDisplayName = $"{lastOwner.RiderName} [Deleted, Former ID: {request.ActualRiderWorkingId}]";
                                }
                                else
                                {
                                    actualRiderDisplayName = $"{lastOwner.RiderName} [Former ID: {request.ActualRiderWorkingId}]";
                                }
                            }
                        }
                    }
                }
            }

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
                new Error("ServerError", ex.InnerException.Message, 500));
        }
    }



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

        // ✅ NEW: Check deleted employees first
        if (originalRiderIqamaNo.HasValue)
        {
            var deletedEmployee = await _dbcontext.DeletedEmployees
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.IqamaNo == originalRiderIqamaNo.Value,
                                    cancellationToken);

            if (deletedEmployee != null)
            {
                // Check if rider was restored
                var restoredRider = await _dbcontext.RiderDetails
                    .AsNoTracking()
                    .Include(r => r.Employee)
                    .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == originalRiderIqamaNo.Value,
                                        cancellationToken);

                if (restoredRider != null)
                {
                    return $"{restoredRider.Employee.NameEN} [Former ID: {actualRiderWorkingId}, Current ID: {restoredRider.WorkingId}]";
                }

                return $"{deletedEmployee.NameEN} [Deleted, Former ID: {actualRiderWorkingId}]";
            }
        }

        // Check WorkingIdHistory
        var historyResult = await _workingIdHistoryService.WhoHasWorkingId(
            actualRiderWorkingId,
            cancellationToken);

        if (historyResult.IsSuccess)
        {
            // If currently assigned
            if (historyResult.Value.IsCurrentlyAssigned &&
                !string.IsNullOrEmpty(historyResult.Value.CurrentRiderName))
            {
                return historyResult.Value.CurrentRiderName;
            }

            // If has previous owners
            if (historyResult.Value.PreviousOwners.Any())
            {
                var lastOwner = historyResult.Value.PreviousOwners.First();

                // Check if last owner is in deleted employees
                var deletedLastOwner = await _dbcontext.DeletedEmployees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.IqamaNo == lastOwner.RiderIqamaNo,
                                        cancellationToken);

                if (deletedLastOwner != null)
                {
                    return $"{lastOwner.RiderName} [Deleted, Former ID: {actualRiderWorkingId}]";
                }

                return $"{lastOwner.RiderName} [Former WorkingId: {actualRiderWorkingId}]";
            }
        }

        // Check if IqamaNo exists in regular employees
        if (originalRiderIqamaNo.HasValue)
        {
            var employeeByIqama = await _dbcontext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IqamaNo == originalRiderIqamaNo.Value,
                    cancellationToken);

            if (employeeByIqama != null)
            {
                return $"{employeeByIqama.NameEN} [IqamaNo: {originalRiderIqamaNo}]";
            }

            return $"Former Rider [IqamaNo: {originalRiderIqamaNo}]";
        }

        // Last resort fallback
        return $"Unassigned WorkingId [{actualRiderWorkingId}]";
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


}