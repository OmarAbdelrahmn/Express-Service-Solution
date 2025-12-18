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

            if (ownershipInfo.IsSuccess && ownershipInfo.Value.IsCurrentlyAssigned)
            {
                var riderResult = await _workingIdHistoryService.GetRiderByWorkingId(
                    request.ActualRiderWorkingId,
                    cancellationToken);

                if (riderResult.IsSuccess)
                {
                    actualRider = riderResult.Value;
                    originalRiderIqamaNo = ownershipInfo.Value.CurrentRiderIqamaNo;
                }
            }
            else if (ownershipInfo.IsSuccess && ownershipInfo.Value.PreviousOwners.Any())
            {
                var lastOwner = ownershipInfo.Value.PreviousOwners.First();
                originalRiderIqamaNo = lastOwner.RiderIqamaNo;
            }

            var substituteRider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == request.SubstituteWorkingId,
                                   cancellationToken);

            if (substituteRider is null)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("the substitution rider is Not Found", "Substitute rider not found", 404));

            if (substituteRider.WorkingId == request.ActualRiderWorkingId)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("InvalidOperation", "Cannot substitute with same WorkingId", 400));

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

            var actualRiderName = actualRider?.Employee?.NameEN
                ?? (originalRiderIqamaNo.HasValue
                    ? $"[IqamaNo: {originalRiderIqamaNo}]"
                    : $"[WorkingId {request.ActualRiderWorkingId}]");

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
                    ? $"[IqamaNo: {substitution.OriginalRiderIqamaNo}]"
                    : $"[WorkingId {substitution.ActualRiderWorkingId}]");

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
                        ? $"[IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"[WorkingId {s.ActualRiderWorkingId}]");

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
                        ? $"[IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"[WorkingId {s.ActualRiderWorkingId}]");

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
                        ? $"[IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"[WorkingId {s.ActualRiderWorkingId}]");

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
                        ? $"[IqamaNo: {s.OriginalRiderIqamaNo}]"
                        : $"[WorkingId {s.ActualRiderWorkingId}]");

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