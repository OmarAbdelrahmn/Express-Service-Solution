using Application.Abstraction;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service;

public class HungerDisabilityService(ApplicationDbcontext dbcontext) : IHungerDisabilityService
{
    private const int DAILY_TARGET = 14;

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

            var allRiders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                    .ThenInclude(e => e.Housing)
                .Include(r => r.Company)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ridersByWorkingId = allRiders
                .Where(r => !string.IsNullOrWhiteSpace(r.WorkingId))
                .ToDictionary(r => r.WorkingId!, r => r);

            var activeSubstitutions = await dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .Include(s => s.SubstituteRider)
                    .ThenInclude(r => r.Employee)
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

                    if (!ridersByWorkingId.TryGetValue(rowData.ActualWorkingId!, out var actualRider))
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"Rider with Working ID {rowData.ActualWorkingId} not found in database"));
                        continue;
                    }

                    var existingRecord = await dbcontext.Set<HungerDisability>()
                        .AnyAsync(h => h.ActualRiderId == actualRider.Id && h.ShiftDate == shiftDate, cancellationToken);

                    if (existingRecord)
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"Record already exists for rider {actualRider.Employee.NameEN} on {shiftDate}"));
                        continue;
                    }

                    if (recordsToAdd.Any(r => r.ActualRiderId == actualRider.Id && r.ShiftDate == shiftDate))
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"Duplicate entry in Excel for rider {actualRider.Employee.NameEN} on {shiftDate}"));
                        continue;
                    }

                    RiderDetails? substituteRider = null;
                    if (substitutionDict.TryGetValue(rowData.ActualWorkingId!, out var substitution))
                    {
                        substituteRider = substitution.SubstituteRider;
                    }

                    var record = new HungerDisability
                    {
                        ActualRiderId = actualRider.Id,
                        ActualWorkingId = actualRider.WorkingId!,
                        SubstituteRiderId = substituteRider?.Id,
                        SubstituteWorkingId = substituteRider?.WorkingId,
                        ShiftDate = shiftDate,
                        Days = rowData.Days!.Value,
                        CompanyId = actualRider.CompanyId,
                        AcceptedDailyOrders = rowData.AcceptedDailyOrders!.Value,
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    };

                    recordsToAdd.Add(record);

                    if (substituteRider != null)
                    {
                        errors.Add(new ImportError(rowNumber, rowData.ActualWorkingId!,
                            $"ℹ️ INFO: Disabled rider {actualRider.Employee.NameEN} has substitute {substituteRider.Employee.NameEN} (ID: {substituteRider.WorkingId})"));
                    }
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

            // Get substitute rider information
            var substituteIds = records.Where(r => r.SubstituteRiderId.HasValue)
                .Select(r => r.SubstituteRiderId!.Value).Distinct().ToList();

            var substituteRiders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Where(r => substituteIds.Contains(r.Id))
                .AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => r, cancellationToken);

            // Calculate the last shift date (one day before endDate)
            var lastShiftDate = endDate.AddDays(-1);

            // Get last day orders for all riders
            var lastDayOrders = await dbcontext.Set<HungerDisability>()
                .Where(h => h.ShiftDate == lastShiftDate &&
                           records.Select(r => r.ActualRiderId).Contains(h.ActualRiderId))
                .AsNoTracking()
                .ToDictionaryAsync(h => h.ActualRiderId, h => h.AcceptedDailyOrders, cancellationToken);

            // Group by rider and housing
            var aggregated = records.GroupBy(r => new
            {
                r.ActualRiderId,
                r.ActualWorkingId,
                RiderNameEN = r.Rider.Employee.NameEN,
                RiderNameAR = r.Rider.Employee.NameAR,
                HousingId = r.Rider.Employee.HousingId,
                HousingName = r.Rider.Employee.Housing?.Name,
                r.CompanyId,
                CompanyName = r.Company.Name,
                r.SubstituteRiderId,
                r.SubstituteWorkingId
            }).Select(g =>
            {
                var totalDays = g.Sum(x => x.Days);
                var totalOrders = g.Sum(x => x.AcceptedDailyOrders);
                var days = (endDate.DayNumber - startDate.DayNumber);

                var target = days * DAILY_TARGET;


                var difference = totalOrders - target;
                var performancePercentage = target > 0 ? Math.Round((decimal)totalOrders / target * 100, 2) : 0;

                // Get substitute info
                var firstRecord = g.First();
                string? substituteNameEN = null;
                string? substituteNameAR = null;
                if (firstRecord.SubstituteRiderId.HasValue &&
                    substituteRiders.TryGetValue(firstRecord.SubstituteRiderId.Value, out var sub))
                {
                    substituteNameEN = sub.Employee.NameEN;
                    substituteNameAR = sub.Employee.NameAR;
                }

                // Get last day orders
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
                    HousingId: g.Key.HousingId,
                    HousingName: g.Key.HousingName ?? "No Housing",
                    TotalDays: totalDays,
                    TotalOrders: totalOrders,
                    Target: target,
                    DifferenceFromTarget: difference,
                    PerformancePercentage: performancePercentage,
                    PerformanceStatus: difference >= 0 ? "✅ Above or Met Target" : "❌ Below Target",
                    LastDayOrders: lastDayOrderCount,
                    RecordCount: g.Count()
                );
            })
            .OrderBy(x => x.HousingName)
            .ThenBy(x => x.ActualWorkingId)
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
                    .FirstOrDefaultAsync(r => r.Id == firstRecord.SubstituteRiderId.Value, cancellationToken);
            }

            // Calculate the last shift date (one day before endDate)
            var lastShiftDate = endDate.AddDays(-1);
            var lastDayRecord = records.FirstOrDefault(r => r.ShiftDate == lastShiftDate);
            var lastDayOrders = lastDayRecord?.AcceptedDailyOrders ?? 0;

            var totalDays = records.Sum(x => x.Days);
            var totalOrders = records.Sum(x => x.AcceptedDailyOrders);
            var days = (endDate.DayNumber - startDate.DayNumber);
            var target = days * DAILY_TARGET;
            var difference = totalOrders - target;
            var performancePercentage = target > 0 ? Math.Round((decimal)totalOrders / target * 100, 2) : 0;

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
                HousingId: firstRecord.Rider.Employee.HousingId,
                HousingName: firstRecord.Rider.Employee.Housing?.Name ?? "No Housing",
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

    // Helper Methods
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