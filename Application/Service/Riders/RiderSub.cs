using Application.Abstraction;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Riders;

public class RiderSub(ApplicationDbcontext dbcontext) : IRiderSub
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<RiderSubstitutionResponse>> StartSubstitution(
        StartSubstitutionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Get actual rider by WorkingId
            var actualRider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == request.ActualRiderWorkingId, cancellationToken);

            if (actualRider is null)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("NotFound", "Actual rider not found", 404));

            // Get substitute by WorkingId
            var substituteRider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == request.SubstituteWorkingId, cancellationToken);

            if (substituteRider is null)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("NotFound", "Substitute working ID not found", 404));

            // Prevent substituting with self
            if (substituteRider.WorkingId == actualRider.WorkingId)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("InvalidOperation", "Cannot substitute with own working ID", 400));

            // Check active substitution by ActualRider numeric Id
            var hasActiveSubstitution = await _dbcontext.RiderShiftSubstitutions
                .AnyAsync(s => s.ActualRiderId == actualRider.Id && s.IsActive, cancellationToken);

            if (hasActiveSubstitution)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("AlreadyExists", "Rider already has an active substitution", 400));

            // Create new substitution
            var substitution = new RiderShiftSubstitution
            {
                ActualRiderId = actualRider.Id,
                ActualRiderWorkingId = actualRider.WorkingId!,
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

            // Prepare response
            var response = new RiderSubstitutionResponse(
                substitution.Id,
                actualRider.Employee.NameEN,
                actualRider.WorkingId!,
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
                .FirstOrDefaultAsync(s => s.ActualRider.WorkingId == WorkingId && s.IsActive,
                    cancellationToken);

            if (substitution is null)
                return Result.Failure<RiderSubstitutionResponse>(
                    new Error("NotFound", "No active substitution found for this working ID", 404));

            substitution.EndDate = DateTime.UtcNow.AddHours(3);
            substitution.IsActive = false;

            await _dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = new RiderSubstitutionResponse(
                substitution.Id,
                substitution.ActualRider.Employee.NameEN,
                substitution.ActualRider.WorkingId ?? "0",
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
                .Select(s => new RiderSubstitutionResponse(
                    s.Id,
                    s.ActualRider.Employee.NameEN,
                    s.ActualRider.WorkingId ?? "0",
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ))
                .ToListAsync(cancellationToken);

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(substitutions);
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
                .Where(s => s.ActualRiderWorkingId == riderWorkingId || s.SubstituteWorkingId == riderWorkingId)
                .OrderByDescending(s => s.StartDate)
                .Select(s => new RiderSubstitutionResponse(
                    s.Id,
                    s.ActualRider.Employee.NameEN,
                    s.ActualRider.WorkingId ?? "0",
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ))
                .ToListAsync(cancellationToken);

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(substitutions);
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
                .Select(s => new RiderSubstitutionResponse(
                    s.Id,
                    s.ActualRider.Employee.NameEN,
                    s.ActualRider.WorkingId ?? "0",
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ))
                .ToListAsync(cancellationToken);

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(substitutions);
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
                .Select(s => new RiderSubstitutionResponse(
                    s.Id,
                    s.ActualRider.Employee.NameEN,
                    s.ActualRider.WorkingId ?? "0",
                    s.SubstituteRider.Employee.NameEN,
                    s.SubstituteWorkingId,
                    s.StartDate,
                    s.EndDate,
                    s.Reason,
                    s.IsActive
                ))
                .ToListAsync(cancellationToken);

            return Result.Success<IEnumerable<RiderSubstitutionResponse>>(substitutions);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderSubstitutionResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }
}