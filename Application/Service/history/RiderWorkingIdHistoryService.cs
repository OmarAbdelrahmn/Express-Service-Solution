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

public class RiderWorkingIdHistoryService(ApplicationDbcontext dbcontext)
    : IRiderWorkingIdHistoryService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result> RecordWorkingIdChange(
        long riderIqamaNo,
        string newWorkingId,
        int newCompanyId,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify rider exists
            var riderExists = await _dbcontext.Employees
                .AnyAsync(e => e.IqamaNo == riderIqamaNo, cancellationToken);

            if (!riderExists)
                return Result.Failure(new Error("NotFound", "Rider not found", 404));

            // Check if this is already the active WorkingId for this rider
            var existingActive = await _dbcontext.RiderWorkingIdHistories
                .FirstOrDefaultAsync(h =>
                    h.RiderIqamaNo == riderIqamaNo &&
                    h.WorkingId == newWorkingId &&
                    h.IsActive,
                    cancellationToken);

            if (existingActive != null)
            {
                // Already active, no change needed
                return Result.Success();
            }

            // Deactivate all previous WorkingIds for this rider
            var previousWorkingIds = await _dbcontext.RiderWorkingIdHistories
                .Where(h => h.RiderIqamaNo == riderIqamaNo && h.IsActive)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow.AddHours(3);

            foreach (var history in previousWorkingIds)
            {
                history.IsActive = false;
                history.EndDate = now;
            }

            // Create new history record
            var newHistory = new RiderWorkingIdHistory
            {
                RiderIqamaNo = riderIqamaNo,
                WorkingId = newWorkingId,
                CompanyId = newCompanyId,
                StartDate = now,
                IsActive = true,
                Notes = notes
            };

            await _dbcontext.RiderWorkingIdHistories.AddAsync(newHistory, cancellationToken);
            await _dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<List<WorkingIdHistoryResponse>>> GetRiderWorkingIdHistory(
        long riderIqamaNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var history = await _dbcontext.RiderWorkingIdHistories
                .Include(h => h.Company)
                .Where(h => h.RiderIqamaNo == riderIqamaNo)
                .OrderByDescending(h => h.StartDate)
                .Select(h => new WorkingIdHistoryResponse(
                    h.WorkingId,
                    h.Company.Name,
                    h.StartDate,
                    h.EndDate,
                    h.IsActive,
                    h.Notes
                ))
                .ToListAsync(cancellationToken);

            return Result.Success(history);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<WorkingIdHistoryResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<WorkingIdOwnershipInfo>> WhoHasWorkingId(
        string workingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var allHistory = await _dbcontext.RiderWorkingIdHistories
                .Include(h => h.Employee)
                .Include(h => h.Company)
                .Where(h => h.WorkingId == workingId)
                .OrderByDescending(h => h.StartDate)
                .ToListAsync(cancellationToken);

            var currentOwner = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);


            var previousOwners = allHistory
                .Where(h => !h.IsActive && h.EndDate.HasValue)
                .Select(h => new PreviousOwner(
                    h.RiderIqamaNo,
                    h.Employee.NameEN,
                    h.Company.Name,
                    h.StartDate,
                    h.EndDate.Value
                ))
                .ToList();

            var info = new WorkingIdOwnershipInfo(
                workingId,
                currentOwner?.EmployeeIqamaNo,
                currentOwner?.Employee?.NameEN,
                currentOwner?.Company?.Name,
                currentOwner != null,
                previousOwners
            );

            return Result.Success(info);
        }
        catch (Exception ex)
        {
            return Result.Failure<WorkingIdOwnershipInfo>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<List<string>>> GetAllWorkingIdsForRider(
        long riderIqamaNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var workingIds = await _dbcontext.RiderWorkingIdHistories
                .Where(h => h.RiderIqamaNo == riderIqamaNo)
                .Select(h => h.WorkingId)
                .Distinct()
                .ToListAsync(cancellationToken);

            return Result.Success(workingIds);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<string>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<RiderDetails?>> GetRiderByWorkingId(
        string workingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // First try: Current owner from history
            var currentHistory = await _dbcontext.RiderWorkingIdHistories
                .Include(h => h.Employee)
                    .ThenInclude(e => e.RiderDetails)
                        .ThenInclude(rd => rd.Company)
                .Include(h => h.Employee)
                    .ThenInclude(e => e.RiderDetails)
                        .ThenInclude(rd => rd.Employee)
                .FirstOrDefaultAsync(h => h.WorkingId == workingId && h.IsActive,
                    cancellationToken);

            if (currentHistory?.Employee?.RiderDetails != null)
                return Result.Success<RiderDetails?>(currentHistory.Employee.RiderDetails);

            // Fallback: Direct lookup
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Company)
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

            return Result.Success<RiderDetails?>(rider);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderDetails?>(
                new Error("ServerError", ex.Message, 500));
        }
    }
}