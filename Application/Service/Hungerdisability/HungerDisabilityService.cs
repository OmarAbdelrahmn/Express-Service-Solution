using Application.Abstraction;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Hungerdisa;

public class HungerDisabilityService(
    ApplicationDbcontext dbcontext,
    IRiderWorkingIdHistoryService workingIdHistoryService) : IHungerDisabilityService
{
    private const int DAILY_TARGET = 14;

    public async Task<Result<DeletionResult>> DeleteAllByDateAsync(
    DateOnly shiftDate,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await dbcontext.Set<HungerDisability>()
                .Where(h => h.ShiftDate == shiftDate)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<DeletionResult>(
                    new Error("NotFound", $"No records found for date {shiftDate}", 404));
            }

            var count = records.Count;
            dbcontext.Set<HungerDisability>().RemoveRange(records);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new DeletionResult(
                DeletedCount: count,
                Message: $"Successfully deleted all {count} records for {shiftDate}",
                Details: new List<string> { $"Deleted {count} rider records from {shiftDate}" }
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DeletionResult>(
                new Error("ServerError", $"Error deleting records: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// Delete a specific rider's record for a specific day
    /// </summary>
    public async Task<Result<DeletionResult>> DeleteByRiderAndDateAsync(
        string actualWorkingId,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(h => h.ActualWorkingId == actualWorkingId && h.ShiftDate == shiftDate)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<DeletionResult>(
                    new Error("NotFound",
                        $"No record found for rider {actualWorkingId} on {shiftDate}", 404));
            }

            var riderName = records.First().Rider.Employee.NameEN;
            var count = records.Count;

            dbcontext.Set<HungerDisability>().RemoveRange(records);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new DeletionResult(
                DeletedCount: count,
                Message: $"Successfully deleted record for {riderName} ({actualWorkingId}) on {shiftDate}",
                Details: new List<string>
                {
                $"Rider: {riderName}",
                $"Working ID: {actualWorkingId}",
                $"Date: {shiftDate}",
                $"Records deleted: {count}"
                }
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DeletionResult>(
                new Error("ServerError", $"Error deleting rider record: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// Delete all records within a date range
    /// </summary>
    public async Task<Result<DeletionResult>> DeleteAllByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<DeletionResult>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var records = await dbcontext.Set<HungerDisability>()
                .Where(h => h.ShiftDate >= startDate && h.ShiftDate <= endDate)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<DeletionResult>(
                    new Error("NotFound",
                        $"No records found between {startDate} and {endDate}", 404));
            }

            var count = records.Count;
            var uniqueRiders = records.Select(r => r.ActualWorkingId).Distinct().Count();
            var dateRange = records.Select(r => r.ShiftDate).Distinct().OrderBy(d => d).ToList();

            dbcontext.Set<HungerDisability>().RemoveRange(records);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new DeletionResult(
                DeletedCount: count,
                Message: $"Successfully deleted {count} records from {startDate} to {endDate}",
                Details: new List<string>
                {
                $"Total records deleted: {count}",
                $"Unique riders affected: {uniqueRiders}",
                $"Date range: {startDate} to {endDate}",
                $"Days covered: {dateRange.Count}"
                }
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DeletionResult>(
                new Error("ServerError", $"Error deleting records: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// Delete a specific rider's records within a date range
    /// </summary>
    public async Task<Result<DeletionResult>> DeleteByRiderAndDateRangeAsync(
        string actualWorkingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<DeletionResult>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(h => h.ActualWorkingId == actualWorkingId &&
                           h.ShiftDate >= startDate &&
                           h.ShiftDate <= endDate)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<DeletionResult>(
                    new Error("NotFound",
                        $"No records found for rider {actualWorkingId} between {startDate} and {endDate}", 404));
            }

            var riderName = records.First().Rider.Employee.NameEN;
            var count = records.Count;
            var totalDays = records.Sum(r => r.Days);
            var totalOrders = records.Sum(r => r.AcceptedDailyOrders);
            var dates = records.Select(r => r.ShiftDate).Distinct().OrderBy(d => d).ToList();

            dbcontext.Set<HungerDisability>().RemoveRange(records);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new DeletionResult(
                DeletedCount: count,
                Message: $"Successfully deleted {count} records for {riderName} ({actualWorkingId}) from {startDate} to {endDate}",
                Details: new List<string>
                {
                $"Rider: {riderName}",
                $"Working ID: {actualWorkingId}",
                $"Records deleted: {count}",
                $"Date range: {startDate} to {endDate}",
                $"Total days removed: {totalDays}",
                $"Total orders removed: {totalOrders}",
                $"Specific dates: {string.Join(", ", dates)}"
                }
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DeletionResult>(
                new Error("ServerError", $"Error deleting rider records: {ex.Message}", 500));
        }
    }

    public async Task<Result<HungerDisabilityImportResult>> ImportFromExcelAsync(
        Stream excelStream,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ImportError>();
        var successCount = 0;
        var totalRecords = 0;

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheet(1);

            var columnMapping = FindColumnIndices(worksheet);
            if (!columnMapping.IsValid)
            {
                return Result.Failure<HungerDisabilityImportResult>(
                    new Error("InvalidExcel", columnMapping.ErrorMessage!, 400));
            }

            var rows = worksheet.RowsUsed().Skip(1);
            totalRecords = rows.Count();

            // Load all riders with their details
            var allRiders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                    .ThenInclude(e => e.Housing)
                .Include(r => r.Company)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ridersByWorkingId = allRiders
                .Where(r => !string.IsNullOrWhiteSpace(r.WorkingId))
                .ToDictionary(r => r.WorkingId!, r => r);

            // Get active substitutions for fallback
            var activeSubstitutions = await dbcontext.Set<RiderShiftSubstitution>()
            .Where(s => s.IsActive)
            .Include(s => s.SubstituteRider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.SubstituteRider)
                .ThenInclude(r => r.Company)
            .AsNoTracking()
            .ToListAsync(cancellationToken);


            var substitutionDict = activeSubstitutions
                .ToDictionary(s => s.ActualRiderWorkingId, s => s);

            var recordsToAdd = new List<HungerDisability>();
            var rowNumber = 1;

            foreach (var row in rows)
            {
                rowNumber++;
                try
                {
                    var rowData = ParseExcelRow(row, columnMapping, rowNumber);

                    if (!rowData.IsValid)
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId ?? "N/A", rowData.ErrorMessage!));
                        continue;
                    }



                    var (actualRiderId, substituteRiderId, riderDetails, resolutionMethod) =
                    await ResolveWorkingIdAsync(
                        rowData.ActualWorkingId!,
                        ridersByWorkingId,
                        substitutionDict,
                        cancellationToken);

                    if (actualRiderId == null || riderDetails == null)
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"Rider with Working ID {rowData.ActualWorkingId} not found in database or history"));
                        continue;
                    }

                    var existingRecord = await dbcontext.Set<HungerDisability>()
                   .AnyAsync(h => h.ActualWorkingId == actualRiderId && h.ShiftDate == shiftDate,
                       cancellationToken);

                    if (existingRecord)
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"Record already exists for rider {riderDetails.Employee.NameEN} on {shiftDate}"));
                        continue;
                    }

                    if (recordsToAdd.Any(r => r.ActualWorkingId == actualRiderId && r.ShiftDate == shiftDate))
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"Duplicate entry in Excel for this rider on {shiftDate}"));
                        continue;
                    }
                    var id = await dbcontext.RiderDetails.Where(c => c.WorkingId == actualRiderId).Select(c => c.Id).FirstOrDefaultAsync(cancellationToken);

                    var record = new HungerDisability
                    {
                        ActualRiderId = id,                    // ✅ Disabled rider's ID
                        ActualWorkingId = rowData.ActualWorkingId!,       // ✅ From Excel (disabled rider's WorkingId)
                        SubstituteRiderId = substituteRiderId,            // ✅ Substitute's ID (if exists)
                        SubstituteWorkingId = substituteRiderId.HasValue
                         ? riderDetails.WorkingId
                         : null,                                        // ✅ Substitute's WorkingId
                        ShiftDate = shiftDate,
                        Days = rowData.Days!.Value,
                        CompanyId = riderDetails.CompanyId,               // Use substitute's company for reporting
                        AcceptedDailyOrders = rowData.AcceptedDailyOrders!.Value,
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    };

                    recordsToAdd.Add(record);

                    // Add informational message about resolution
                    if (resolutionMethod == "WorkingIdHistory")
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"ℹ️ INFO: WorkingId {rowData.ActualWorkingId} resolved via history to {riderDetails.Employee.NameEN} (Current ID: {riderDetails.WorkingId})"));
                    }
                    //else if (resolutionMethod == "Substitution")
                    //{
                    //    errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                    //        $"ℹ️ INFO: Disabled rider WorkingId {rowData.ActualWorkingId} has substitute {riderDetails.Employee.NameEN} (ID: {riderDetails.WorkingId})"));
                    //}
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError(rowNumber, "N/A", $"Error processing row: {ex.Message}"));
                }
            }

            if (recordsToAdd.Any())
            {
                await dbcontext.Set<HungerDisability>().AddRangeAsync(recordsToAdd, cancellationToken);
                await dbcontext.SaveChangesAsync(cancellationToken);
                successCount = recordsToAdd.Count;
            }

            return Result.Success(new HungerDisabilityImportResult(totalRecords, successCount, errors.Count, errors));
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerDisabilityImportResult>(
                new Error("ServerError", $"Error reading Excel file: {ex.Message}", 500));
        }
    }

    private async Task<(
            string? actualRiderId,           // Disabled rider's ID (for record storage)
            int? substituteRiderId,      // Substitute rider's ID (who did the work)
            RiderDetails? riderDetails,  // Details to use for Company/Housing
            string resolutionMethod
        )> ResolveWorkingIdAsync(
            string workingId,
            Dictionary<string, RiderDetails> ridersByWorkingId,
            Dictionary<string, RiderShiftSubstitution> substitutionDict,
            CancellationToken cancellationToken)
    {
        // 1. Try substitution system first (disabled rider with active substitute)
        if (substitutionDict.TryGetValue(workingId, out var substitution))
        {
            var substituteRider = substitution.SubstituteRider;
            if (substituteRider == null)
                return (null, null, null, "NotFound");

            // Get the DISABLED rider's ID (not substitute's ID)
            string? actualRiderId = substitution.ActualRiderWorkingId;

            // If ActualRider was deleted but we have their IqamaNo, try to find them
            if (actualRiderId != null && substitution.OriginalRiderIqamaNo.HasValue)
            {
                var actualRider = await dbcontext.RiderDetails
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == substitution.OriginalRiderIqamaNo.Value,
                        cancellationToken);
                if (actualRider != null)
                    actualRiderId = actualRider.WorkingId;
            }

            // If we still can't find the disabled rider, we can't create a valid record
            if (actualRiderId == null)
            {
                return (null, null, null, "NotFound");
            }

            // Return disabled rider's ID for ActualRiderId, substitute's ID separately
            return (
                actualRiderId,      // Disabled rider's ID
                substituteRider.Id,       // Substitute rider's ID
                substituteRider,          // Use substitute's details for Company/Housing
                "Substitution"
            );
        }

        // 2. Try direct lookup (current active WorkingId)
        if (ridersByWorkingId.TryGetValue(workingId, out var directRider))
        {
            return (directRider.WorkingId, null, directRider, "Direct");
        }

        // 3. Try WorkingIdHistory (WorkingId might have been reassigned)
        var historyResult = await workingIdHistoryService.WhoHasWorkingId(workingId, cancellationToken);
        if (historyResult.IsSuccess && historyResult.Value.IsCurrentlyAssigned)
        {
            var currentRiderResult = await workingIdHistoryService.GetRiderByWorkingId(workingId, cancellationToken);
            if (currentRiderResult.IsSuccess && currentRiderResult.Value != null)
            {
                return (currentRiderResult.Value.WorkingId, null, currentRiderResult.Value, "WorkingIdHistory");
            }
        }

        return (null, null, null, "NotFound");
    }
    public async Task<Result<IEnumerable<HungerDisabilityAggregatedResponse>>> GetReportsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<IEnumerable<HungerDisabilityAggregatedResponse>>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Include(h => h.Company)
                .Where(h => h.ShiftDate >= startDate && h.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<IEnumerable<HungerDisabilityAggregatedResponse>>(
                    new Error("NotFound", $"No records found between {startDate} and {endDate}", 404));
            }

            var substituteIds = records.Where(r => r.SubstituteRiderId.HasValue)
                .Select(r => r.SubstituteRiderId!.Value).Distinct().ToList();

            var substituteRiders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .ThenInclude(e => e.Housing)
                .Where(r => substituteIds.Contains(r.Id))
                .AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => r, cancellationToken);

            var lastShiftDate = endDate.AddDays(-1);

            var lastDayOrders = await dbcontext.Set<HungerDisability>()
                .Where(h => h.ShiftDate == lastShiftDate &&
                           records.Select(r => r.ActualRiderId).Contains(h.ActualRiderId))
                .AsNoTracking()
                .ToDictionaryAsync(h => h.ActualRiderId, h => h.AcceptedDailyOrders, cancellationToken);

            var aggregated = records.GroupBy(r => new
            {
                r.ActualRiderId,
                r.ActualWorkingId,
                RiderNameEN = r.Rider.Employee.NameEN,
                RiderNameAR = r.Rider.Employee.NameAR,
                r.CompanyId,
                CompanyName = r.Company.Name,
                r.SubstituteRiderId,
                r.SubstituteWorkingId
                // ❌ REMOVE HousingId and HousingName from here - they should be calculated per record
            }).Select(g =>
            {
                var totalDays = g.Sum(x => x.Days);
                var totalOrders = g.Sum(x => x.AcceptedDailyOrders);
                var days = (endDate.DayNumber - startDate.DayNumber);
                var target = days * DAILY_TARGET;
                var difference = totalOrders - target;
                var performancePercentage = target > 0 ? Math.Round((decimal)totalOrders / target * 100, 2) : 0;

                var firstRecord = g.First();
                string? substituteNameEN = null;
                string? substituteNameAR = null;
                int? housingId = null;
                string? housingName = null;

                // ✅ Determine which housing to use
                if (firstRecord.SubstituteRiderId.HasValue &&
                    substituteRiders.TryGetValue(firstRecord.SubstituteRiderId.Value, out var sub))
                {
                    substituteNameEN = sub.Employee.NameEN;
                    substituteNameAR = sub.Employee.NameAR;
                    // Use substitute's housing
                    housingId = sub.Employee.HousingId;
                    housingName = sub.Employee.Housing?.Name ?? "No Housing";
                }
                else
                {
                    // Use actual rider's housing
                    housingId = firstRecord.Rider.Employee.HousingId;
                    housingName = firstRecord.Rider.Employee.Housing?.Name ?? "No Housing";
                }

                var lastDayOrderCount = lastDayOrders.TryGetValue(g.Key.ActualRiderId, out var orders) ? orders : 0;

                return new HungerDisabilityAggregatedResponse(
                    ActualRiderId: g.Key.ActualRiderId,
                    ActualWorkingId: g.Key.ActualWorkingId,
                    ActualRiderNameEN: g.Key.RiderNameEN,
                    ActualRiderNameAR: g.Key.RiderNameAR,
                    SubstituteRiderId: firstRecord.SubstituteRiderId,
                    SubstituteWorkingId: firstRecord.SubstituteWorkingId,
                    SubstituteRiderNameEN: substituteNameEN,
                    SubstituteRiderNameAR: substituteNameAR,
                    HasSubstitute: firstRecord.SubstituteRiderId.HasValue,
                    HousingId: housingId,
                    HousingName: housingName,
                    TotalDays: totalDays,
                    TotalOrders: totalOrders,
                    Target: target,
                    DifferenceFromTarget: difference,
                    PerformancePercentage: performancePercentage,
                    PerformanceStatus: difference >= 0 ? "✅" : "❌",
                    LastDayOrders: lastDayOrderCount,
                    RecordCount: g.Count()
                );
            })
             .OrderBy(x => x.HousingName)
             .ThenByDescending(x => x.PerformancePercentage)
             .ToList();


            return Result.Success<IEnumerable<HungerDisabilityAggregatedResponse>>(aggregated);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityAggregatedResponse>>(
                new Error("ServerError", $"Error retrieving reports: {ex.Message}", 500));
        }
    }


    public async Task<Result<IEnumerable<HungerDisabilityAggregatedResponse>>> GetReportsByMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startDate = new DateOnly(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            return await GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityAggregatedResponse>>(
                new Error("ServerError", $"Error retrieving monthly reports: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<HungerDisabilityAggregatedResponse>>> GetReportsByYearAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startDate = new DateOnly(year, 1, 1);
            var endDate = new DateOnly(year, 12, 31);
            return await GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityAggregatedResponse>>(
                new Error("ServerError", $"Error retrieving yearly reports: {ex.Message}", 500));
        }
    }

    public async Task<Result<HungerDisabilityAggregatedResponse>> GetReportByRiderAndDateRangeAsync(
        string actualWorkingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<HungerDisabilityAggregatedResponse>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Include(h => h.Company)
                .Where(h => h.ActualWorkingId == actualWorkingId &&
                           h.ShiftDate >= startDate &&
                           h.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<HungerDisabilityAggregatedResponse>(
                    new Error("NotFound", $"No records found for rider {actualWorkingId} between {startDate} and {endDate}", 404));
            }

            var firstRecord = records.First();
            RiderDetails? substituteRider = null;
            if (firstRecord.SubstituteRiderId.HasValue)
            {
                substituteRider = await dbcontext.RiderDetails
                    .Include(r => r.Employee)
                        .ThenInclude(e => e.Housing)  // ✅ ADD THIS LINE
                    .FirstOrDefaultAsync(r => r.Id == firstRecord.SubstituteRiderId.Value, cancellationToken);
            }


            var lastShiftDate = endDate.AddDays(-1);
            var lastDayRecord = records.FirstOrDefault(r => r.ShiftDate == lastShiftDate);
            var lastDayOrders = lastDayRecord?.AcceptedDailyOrders ?? 0;

            var totalDays = records.Sum(x => x.Days);
            var totalOrders = records.Sum(x => x.AcceptedDailyOrders);
            var days = (endDate.DayNumber - startDate.DayNumber);
            var target = days * DAILY_TARGET;
            var difference = totalOrders - target;
            var performancePercentage = target > 0 ? Math.Round((decimal)totalOrders / target * 100, 2) : 0;

            int? housingId;
            string housingName;

            if (substituteRider != null)
            {
                housingId = substituteRider.Employee.HousingId;
                housingName = substituteRider.Employee.Housing?.Name ?? "No Housing";
            }
            else
            {
                housingId = firstRecord.Rider.Employee.HousingId;
                housingName = firstRecord.Rider.Employee.Housing?.Name ?? "No Housing";
            }

            var response = new HungerDisabilityAggregatedResponse(
                ActualRiderId: firstRecord.ActualRiderId,
                ActualWorkingId: firstRecord.ActualWorkingId,
                ActualRiderNameEN: firstRecord.Rider.Employee.NameEN,
                ActualRiderNameAR: firstRecord.Rider.Employee.NameAR,
                SubstituteRiderId: firstRecord.SubstituteRiderId,
                SubstituteWorkingId: firstRecord.SubstituteWorkingId,
                SubstituteRiderNameEN: substituteRider?.Employee.NameEN,
                SubstituteRiderNameAR: substituteRider?.Employee.NameAR,
                HasSubstitute: firstRecord.SubstituteRiderId.HasValue,
                HousingId: housingId,
                HousingName: housingName,
                TotalDays: totalDays,
                TotalOrders: totalOrders,
                Target: target,
                DifferenceFromTarget: difference,
                PerformancePercentage: performancePercentage,
                PerformanceStatus: difference >= 0 ? "✅ Above or Met Target" : "❌ Below Target",
                LastDayOrders: lastDayOrders,
                RecordCount: records.Count
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerDisabilityAggregatedResponse>(
                new Error("ServerError", $"Error retrieving rider report: {ex.Message}", 500));
        }
    }

    public async Task<Result<HungerDisabilityOverallSummary>> GetOverallSummaryAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var aggregatedResult = await GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);

            if (aggregatedResult.IsFailure)
            {
                return Result.Failure<HungerDisabilityOverallSummary>(aggregatedResult.Error);
            }

            var reports = aggregatedResult.Value.ToList();
            var totalRiders = reports.Count;
            var totalDays = reports.Sum(r => r.TotalDays);
            var totalOrders = reports.Sum(r => r.TotalOrders);
            var totalTarget = reports.Sum(r => r.Target);
            var totalDifference = totalOrders - totalTarget;
            var ridersAboveTarget = reports.Count(r => r.DifferenceFromTarget >= 0);
            var ridersBelowTarget = totalRiders - ridersAboveTarget;
            var ridersWithSubstitutes = reports.Count(r => r.HasSubstitute);
            var ridersWithoutSubstitutes = totalRiders - ridersWithSubstitutes;
            var averageOrdersPerRider = totalRiders > 0 ? Math.Round((decimal)totalOrders / totalRiders, 2) : 0;
            var averageOrdersPerDay = totalDays > 0 ? Math.Round((decimal)totalOrders / totalDays, 2) : 0;

            var housingBreakdown = reports.GroupBy(r => r.HousingName)
                .Select(g => new HousingSummaryDetail(
                    g.Key,
                    g.Count(),
                    g.Sum(r => r.TotalOrders),
                    g.Count(r => r.DifferenceFromTarget >= 0)
                )).ToList();

            var summary = new HungerDisabilityOverallSummary(
                TotalRiders: totalRiders,
                TotalDays: totalDays,
                TotalOrders: totalOrders,
                TotalTarget: totalTarget,
                TotalDifference: totalDifference,
                RidersAboveTarget: ridersAboveTarget,
                RidersBelowTarget: ridersBelowTarget,
                RidersWithSubstitutes: ridersWithSubstitutes,
                RidersWithoutSubstitutes: ridersWithoutSubstitutes,
                AverageOrdersPerRider: averageOrdersPerRider,
                AverageOrdersPerDay: averageOrdersPerDay,
                OverallPerformanceRate: totalTarget > 0 ? Math.Round((decimal)totalOrders / totalTarget * 100, 2) : 0,
                HousingBreakdown: housingBreakdown,
                TopPerformers: reports.OrderByDescending(r => r.DifferenceFromTarget).Take(5).ToList(),
                BottomPerformers: reports.OrderBy(r => r.DifferenceFromTarget).Take(5).ToList()
            );

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerDisabilityOverallSummary>(
                new Error("ServerError", $"Error calculating summary: {ex.Message}", 500));
        }
    }

    private static ExcelColumnMapping FindColumnIndices(IXLWorksheet worksheet)
    {
        var headerRow = worksheet.FirstRowUsed();
        if (headerRow == null)
        {
            return new ExcelColumnMapping
            {
                IsValid = false,
                ErrorMessage = "Excel file is empty or has no header row"
            };
        }

        var mapping = new ExcelColumnMapping();
        var headerCells = headerRow.CellsUsed().ToList();

        mapping.ActualWorkingIdColumn = FindColumn(headerCells, HungerExcelColumns.ActualWorkingIdColumns);
        mapping.DaysColumn = FindColumn(headerCells, HungerExcelColumns.DaysColumns);
        mapping.AcceptedOrdersColumn = FindColumn(headerCells, HungerExcelColumns.AcceptedOrdersColumns);

        var missingColumns = new List<string>();

        if (mapping.ActualWorkingIdColumn == 0)
            missingColumns.Add($"ActualWorkingId (tried: {string.Join(", ", HungerExcelColumns.ActualWorkingIdColumns)})");
        if (mapping.DaysColumn == 0)
            missingColumns.Add($"Days (tried: {string.Join(", ", HungerExcelColumns.DaysColumns)})");
        if (mapping.AcceptedOrdersColumn == 0)
            missingColumns.Add($"AcceptedOrders (tried: {string.Join(", ", HungerExcelColumns.AcceptedOrdersColumns)})");

        if (missingColumns.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Missing required columns: {string.Join(", ", missingColumns)}";
            return mapping;
        }

        mapping.IsValid = true;
        return mapping;
    }

    private static int FindColumn(List<IXLCell> headerCells, string[] possibleNames)
    {
        foreach (var cell in headerCells)
        {
            var headerValue = cell.Value.ToString().Trim();
            foreach (var possibleName in possibleNames)
            {
                if (headerValue.Equals(possibleName, StringComparison.OrdinalIgnoreCase))
                {
                    return cell.Address.ColumnNumber;
                }
            }
        }
        return 0;
    }

    private static (bool IsValid, string? ActualWorkingId, int? Days, int? AcceptedDailyOrders, string? ErrorMessage)
        ParseExcelRow(IXLRow row, ExcelColumnMapping mapping, int rowNumber)
    {
        try
        {
            var workingIdCell = row.Cell(mapping.ActualWorkingIdColumn).Value;
            var workingId = workingIdCell.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(workingId))
                return (false, null, null, null, "Invalid Working ID");

            var daysCell = row.Cell(mapping.DaysColumn).Value;
            if (!int.TryParse(daysCell.ToString(), out var days) || days <= 0)
                return (false, workingId, null, null, "Invalid Days (must be > 0)");

            var ordersCell = row.Cell(mapping.AcceptedOrdersColumn).Value;
            if (!int.TryParse(ordersCell.ToString(), out var acceptedOrders) || acceptedOrders < 0)
                return (false, workingId, days, null, "Invalid Accepted Orders (must be >= 0)");

            return (true, workingId, days, acceptedOrders, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, null, $"Error parsing row: {ex.Message}");
        }
    }
}