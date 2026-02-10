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


    // RiderWorkingIdHistoryService.cs - Add these methods to your existing service

    public async Task<Result<RiderWorkingIdHistoryReport>> GetRiderHistoryReport(
        long riderIqamaNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get rider information
            var rider = await _dbcontext.Employees
                .Include(e => e.RiderDetails)
                    .ThenInclude(rd => rd.Company)
                .FirstOrDefaultAsync(e => e.IqamaNo == riderIqamaNo && !e.IsDeleted,
                    cancellationToken);

            if (rider == null)
            {
                return Result.Failure<RiderWorkingIdHistoryReport>(
                    new Error("NotFound", "Rider not found or has been deleted", 404));
            }

            // Get all history records for this rider
            var historyRecords = await _dbcontext.RiderWorkingIdHistories
                .Include(h => h.Company)
                .Where(h => h.RiderIqamaNo == riderIqamaNo)
                .OrderByDescending(h => h.StartDate)
                .ToListAsync(cancellationToken);

            if (!historyRecords.Any())
            {
                return Result.Failure<RiderWorkingIdHistoryReport>(
                    new Error("NotFound", "No working ID history found for this rider", 404));
            }

            // Calculate statistics
            var uniqueCompanies = historyRecords
                .Select(h => h.CompanyId)
                .Distinct()
                .Count();

            var uniqueWorkingIds = historyRecords
                .Select(h => h.WorkingId)
                .Distinct()
                .Count();

            // Map history entries
            var historyEntries = historyRecords.Select(h => new WorkingIdHistoryEntry(
                h.Id,
                h.WorkingId,
                h.Company.Name,
                h.CompanyId,
                h.StartDate,
                h.EndDate,
                h.IsActive,
                h.EndDate.HasValue
                    ? (int)(h.EndDate.Value - h.StartDate).TotalDays
                    : (int)(DateTime.UtcNow.AddHours(3) - h.StartDate).TotalDays,
                h.Notes ?? string.Empty
            )).ToList();

            var report = new RiderWorkingIdHistoryReport(
                riderIqamaNo,
                rider.NameEN,
                rider.NameAR,
                rider.RiderDetails?.WorkingId ?? "N/A",
                rider.RiderDetails?.Company?.Name ?? "N/A",
                uniqueCompanies - 1, // Subtract 1 to get number of changes
                uniqueWorkingIds - 1, // Subtract 1 to get number of changes
                historyEntries
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderWorkingIdHistoryReport>(
                new Error("ServerError", $"Error retrieving rider history: {ex.Message}", 500));
        }
    }

    public async Task<Result<SuggestedWorkingIdResponse>> SuggestWorkingIdForCompany(
        long riderIqamaNo,
        int companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get rider information
            var rider = await _dbcontext.Employees
                .Include(e => e.RiderDetails)
                    .ThenInclude(rd => rd.Company)
                .FirstOrDefaultAsync(e => e.IqamaNo == riderIqamaNo && !e.IsDeleted,
                    cancellationToken);

            if (rider == null)
            {
                return Result.Failure<SuggestedWorkingIdResponse>(
                    new Error("NotFound", "Rider not found or has been deleted", 404));
            }

            // Get target company
            var company = await _dbcontext.Companies
                .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

            if (company == null)
            {
                return Result.Failure<SuggestedWorkingIdResponse>(
                    new Error("NotFound", "Company not found", 404));
            }

            // Get all previous working IDs for this company
            var previousWorkingIds = await _dbcontext.RiderWorkingIdHistories
                .Where(h => h.RiderIqamaNo == riderIqamaNo && h.CompanyId == companyId)
                .OrderByDescending(h => h.EndDate ?? DateTime.MaxValue)
                .ToListAsync(cancellationToken);

            string? suggestedWorkingId = null;
            DateTime? lastUsedDate = null;
            int? daysUsed = null;
            string message;
            var allPreviousOptions = new List<PreviousWorkingIdOption>();

            if (previousWorkingIds.Any())
            {
                // Check each previous working ID to see if it's currently in use
                foreach (var history in previousWorkingIds)
                {
                    var isCurrentlyInUse = await _dbcontext.RiderDetails
                        .AnyAsync(rd => rd.WorkingId == history.WorkingId &&
                                       rd.EmployeeIqamaNo != riderIqamaNo,
                                 cancellationToken);

                    string? currentUser = null;
                    if (isCurrentlyInUse)
                    {
                        var currentRider = await _dbcontext.RiderDetails
                            .Include(rd => rd.Employee)
                            .FirstOrDefaultAsync(rd => rd.WorkingId == history.WorkingId,
                                               cancellationToken);
                        currentUser = currentRider?.Employee.NameEN;
                    }

                    var option = new PreviousWorkingIdOption(
                        history.WorkingId,
                        history.StartDate,
                        history.EndDate,
                        history.EndDate.HasValue
                            ? (int)(history.EndDate.Value - history.StartDate).TotalDays
                            : (int)(DateTime.UtcNow.AddHours(3) - history.StartDate).TotalDays,
                        isCurrentlyInUse,
                        currentUser
                    );

                    allPreviousOptions.Add(option);

                    // Set the most recent available ID as suggestion
                    if (suggestedWorkingId == null && !isCurrentlyInUse)
                    {
                        suggestedWorkingId = history.WorkingId;
                        lastUsedDate = history.EndDate ?? history.StartDate;
                        daysUsed = option.DaysUsed;
                    }
                }

                if (suggestedWorkingId != null)
                {
                    message = $"Suggested working ID based on previous assignment to {company.Name}. " +
                             $"Last used {(DateTime.UtcNow.AddHours(3) - lastUsedDate!.Value).Days} days ago.";
                }
                else
                {
                    message = $"Rider previously worked for {company.Name}, but all previous working IDs are currently in use by other riders.";
                }
            }
            else
            {
                message = $"No previous working ID found for {company.Name}. A new working ID needs to be assigned.";
            }

            var response = new SuggestedWorkingIdResponse(
                riderIqamaNo,
                rider.NameEN,
                rider.NameAR,
                companyId,
                company.Name,
                previousWorkingIds.Any(),
                suggestedWorkingId,
                lastUsedDate,
                daysUsed,
                message,
                allPreviousOptions
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<SuggestedWorkingIdResponse>(
                new Error("ServerError", $"Error suggesting working ID: {ex.Message}", 500));
        }
    }
}