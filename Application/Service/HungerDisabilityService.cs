using Application.Abstraction;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service;

public class HungerDisabilityService(ApplicationDbcontext dbcontext) : IHungerDisabilityService
{
    private const int DAILY_TARGET = 15;
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<HungerDisabilityImportResult>> ImportFromExcelAsync(
        Stream excelStream,
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

            // Load all riders with their relationships
            var allRiders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ridersByWorkingId = allRiders
                .Where(r => !string.IsNullOrWhiteSpace(r.WorkingId))
                .ToDictionary(r => r.WorkingId!, r => r);

            // Load active substitutions
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
                        errors.Add(new ImportError(
                            rowNumber,
                            rowData.ActualWorkingId ?? "N/A",
                            rowData.ErrorMessage!));
                        continue;
                    }

                    // Find the actual rider
                    if (!ridersByWorkingId.TryGetValue(rowData.ActualWorkingId!, out var actualRider))
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            rowData.ActualWorkingId!,
                            $"Rider with Working ID {rowData.ActualWorkingId} not found in database"));
                        continue;
                    }

                    // Check if rider is disabled
                    if (actualRider.Employee.Status != "disable")
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            rowData.ActualWorkingId!,
                            $"Rider {actualRider.Employee.NameEN} is not disabled (Status: {actualRider.Employee.Status}). This report is for disabled riders only."));
                        continue;
                    }

                    // Check for existing record
                    var existingRecord = await dbcontext.Set<HungerDisability>()
                        .AnyAsync(h => h.ActualRiderId == actualRider.Id &&
                                      h.ShiftDate == rowData.ShiftDate,
                                 cancellationToken);

                    if (existingRecord)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            rowData.ActualWorkingId!,
                            $"Record already exists for rider {actualRider.Employee.NameEN} on {rowData.ShiftDate}"));
                        continue;
                    }

                    // Check for duplicate in current batch
                    if (recordsToAdd.Any(r => r.ActualRiderId == actualRider.Id &&
                                             r.ShiftDate == rowData.ShiftDate))
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            rowData.ActualWorkingId!,
                            $"Duplicate entry in Excel for rider {actualRider.Employee.NameEN} on {rowData.ShiftDate}"));
                        continue;
                    }

                    // Check for substitution
                    RiderDetails? substituteRider = null;
                    if (substitutionDict.TryGetValue(rowData.ActualWorkingId!, out var substitution))
                    {
                        substituteRider = substitution.SubstituteRider;
                    }

                    // Create HungerDisability record
                    var record = new HungerDisability
                    {
                        ActualRiderId = actualRider.Id,
                        ActualWorkingId = actualRider.WorkingId!,
                        SubstituteRiderId = substituteRider?.Id,
                        SubstituteWorkingId = substituteRider?.WorkingId,
                        ShiftDate = rowData.ShiftDate!.Value,
                        Days = rowData.Days!.Value,
                        CompanyId = actualRider.CompanyId,
                        AcceptedDailyOrders = rowData.AcceptedDailyOrders!.Value,
                        CreatedAt = DateTime.UtcNow
                    };

                    recordsToAdd.Add(record);

                    // Add info message about substitution
                    if (substituteRider != null)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            rowData.ActualWorkingId!,
                            $"ℹ️ INFO: Disabled rider {actualRider.Employee.NameEN} has substitute {substituteRider.Employee.NameEN} (ID: {substituteRider.WorkingId})"));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError(
                        rowNumber,
                        "N/A",
                        $"Error processing row: {ex.Message}"));
                }
            }

            // Save all records
            if (recordsToAdd.Any())
            {
                await dbcontext.Set<HungerDisability>().AddRangeAsync(recordsToAdd, cancellationToken);
                await dbcontext.SaveChangesAsync(cancellationToken);
                successCount = recordsToAdd.Count;
            }

            var result = new HungerDisabilityImportResult(
                totalRecords,
                successCount,
                errors.Count,
                errors
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerDisabilityImportResult>(
                new Error("ServerError", $"Error reading Excel file: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetAllReportsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(h => h.Company)
                .AsNoTracking()
                .OrderByDescending(h => h.ShiftDate)
                .ThenBy(h => h.ActualWorkingId)
                .ToListAsync(cancellationToken);

            var responses = await MapToResponsesAsync(records, cancellationToken);
            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                new Error("ServerError", $"Error retrieving reports: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetReportsByDateAsync(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(h => h.Company)
                .Where(h => h.ShiftDate == shiftDate)
                .AsNoTracking()
                .OrderBy(h => h.ActualWorkingId)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                    new Error("NotFound", $"No records found for date {shiftDate}", 404));
            }

            var responses = await MapToResponsesAsync(records, cancellationToken);
            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                new Error("ServerError", $"Error retrieving reports: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetReportsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(h => h.Company)
                .Where(h => h.ShiftDate >= startDate && h.ShiftDate <= endDate)
                .AsNoTracking()
                .OrderBy(h => h.ShiftDate)
                .ThenBy(h => h.ActualWorkingId)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                    new Error("NotFound", $"No records found between {startDate} and {endDate}", 404));
            }

            var responses = await MapToResponsesAsync(records, cancellationToken);
            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                new Error("ServerError", $"Error retrieving reports: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetReportsByRiderAsync(
        string actualWorkingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(h => h.Company)
                .Where(h => h.ActualWorkingId == actualWorkingId)
                .AsNoTracking()
                .OrderByDescending(h => h.ShiftDate)
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                    new Error("NotFound", $"No records found for rider {actualWorkingId}", 404));
            }

            var responses = await MapToResponsesAsync(records, cancellationToken);
            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HungerDisabilityReportResponse>>(
                new Error("ServerError", $"Error retrieving reports: {ex.Message}", 500));
        }
    }

    public async Task<Result<HungerDisabilitySummary>> GetSummaryByRiderAsync(
        string actualWorkingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(h => h.Company)
                .Where(h => h.ActualWorkingId == actualWorkingId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<HungerDisabilitySummary>(
                    new Error("NotFound", $"No records found for rider {actualWorkingId}", 404));
            }

            var summary = await CalculateSummaryAsync(records, cancellationToken);
            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerDisabilitySummary>(
                new Error("ServerError", $"Error calculating summary: {ex.Message}", 500));
        }
    }

    public async Task<Result<HungerDisabilitySummary>> GetSummaryByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<HungerDisabilitySummary>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var records = await dbcontext.Set<HungerDisability>()
                .Include(h => h.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(h => h.Company)
                .Where(h => h.ShiftDate >= startDate && h.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!records.Any())
            {
                return Result.Failure<HungerDisabilitySummary>(
                    new Error("NotFound", $"No records found between {startDate} and {endDate}", 404));
            }

            var summary = await CalculateSummaryAsync(records, cancellationToken);
            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerDisabilitySummary>(
                new Error("ServerError", $"Error calculating summary: {ex.Message}", 500));
        }
    }

    // Helper Methods
    private async Task<IEnumerable<HungerDisabilityReportResponse>> MapToResponsesAsync(
        List<HungerDisability> records,
        CancellationToken cancellationToken)
    {
        var responses = new List<HungerDisabilityReportResponse>();

        // Get all substitute rider IDs
        var substituteIds = records
            .Where(r => r.SubstituteRiderId.HasValue)
            .Select(r => r.SubstituteRiderId!.Value)
            .Distinct()
            .ToList();

        // Load substitute riders
        var substituteRiders = await dbcontext.RiderDetails
            .Include(r => r.Employee)
            .Where(r => substituteIds.Contains(r.Id))
            .AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r, cancellationToken);

        foreach (var record in records)
        {
            RiderDetails? substituteRider = null;
            if (record.SubstituteRiderId.HasValue &&
                substituteRiders.TryGetValue(record.SubstituteRiderId.Value, out var sub))
            {
                substituteRider = sub;
            }

            var targetAnalysis = AnalyzeTarget(record.AcceptedDailyOrders);

            var response = new HungerDisabilityReportResponse(
                Id: record.Id,
                ActualRiderId: record.ActualRiderId,
                ActualWorkingId: record.ActualWorkingId,
                ActualRiderNameEN: record.Rider.Employee.NameEN,
                ActualRiderNameAR: record.Rider.Employee.NameAR,
                RiderStatus: record.Rider.Employee.Status,
                SubstituteRiderId: record.SubstituteRiderId,
                SubstituteWorkingId: record.SubstituteWorkingId,
                SubstituteRiderNameEN: substituteRider?.Employee.NameEN,
                SubstituteRiderNameAR: substituteRider?.Employee.NameAR,
                HasSubstitute: record.SubstituteRiderId.HasValue,
                ShiftDate: record.ShiftDate,
                Days: record.Days,
                CompanyId: record.CompanyId,
                CompanyName: record.Company.Name,
                AcceptedDailyOrders: record.AcceptedDailyOrders,
                DailyTarget: DAILY_TARGET,
                TargetAchieved: targetAnalysis.Achieved,
                DifferenceFromTarget: targetAnalysis.Difference,
                PerformancePercentage: targetAnalysis.Percentage,
                PerformanceStatus: targetAnalysis.Status,
                PerformanceNote: targetAnalysis.Note,
                CreatedAt: record.CreatedAt
            );

            responses.Add(response);
        }

        return responses;
    }

    private async Task<HungerDisabilitySummary> CalculateSummaryAsync(
        List<HungerDisability> records,
        CancellationToken cancellationToken)
    {
        var totalDays = records.Sum(r => r.Days);
        var totalOrders = records.Sum(r => r.AcceptedDailyOrders);
        var totalRecords = records.Count;

        var daysWithSubstitute = records.Count(r => r.SubstituteRiderId.HasValue);
        var daysWithoutSubstitute = totalRecords - daysWithSubstitute;

        var daysMetTarget = records.Count(r => r.AcceptedDailyOrders >= DAILY_TARGET);
        var daysFailedTarget = totalRecords - daysMetTarget;

        var averageOrders = totalRecords > 0 ? (decimal)totalOrders / totalRecords : 0;
        var targetAchievementRate = totalRecords > 0 ? (decimal)daysMetTarget / totalRecords * 100 : 0;

        var riderGroups = records.GroupBy(r => new
        {
            r.ActualRiderId,
            r.ActualWorkingId,
            RiderName = r.Rider.Employee.NameEN
        }).Select(g => new RiderSummaryDetail(
            g.Key.ActualRiderId,
            g.Key.ActualWorkingId,
            g.Key.RiderName,
            g.Count(),
            g.Sum(r => r.Days),
            g.Sum(r => r.AcceptedDailyOrders),
            g.Count(r => r.AcceptedDailyOrders >= DAILY_TARGET),
            g.Count(r => r.AcceptedDailyOrders < DAILY_TARGET)
        )).ToList();

        var companyGroups = records.GroupBy(r => r.Company.Name)
            .Select(g => new CompanySummaryDetail(
                g.Key,
                g.Count(),
                g.Sum(r => r.AcceptedDailyOrders),
                g.Count(r => r.AcceptedDailyOrders >= DAILY_TARGET)
            )).ToList();

        return new HungerDisabilitySummary(
            TotalRecords: totalRecords,
            TotalDays: totalDays,
            TotalOrders: totalOrders,
            AverageOrdersPerDay: Math.Round(averageOrders, 2),
            DailyTarget: DAILY_TARGET,
            DaysMetTarget: daysMetTarget,
            DaysFailedTarget: daysFailedTarget,
            TargetAchievementRate: Math.Round(targetAchievementRate, 2),
            DaysWithSubstitute: daysWithSubstitute,
            DaysWithoutSubstitute: daysWithoutSubstitute,
            RiderDetails: riderGroups,
            CompanyBreakdown: companyGroups
        );
    }

    private static TargetAnalysis AnalyzeTarget(int acceptedOrders)
    {
        var difference = acceptedOrders - DAILY_TARGET;
        var percentage = (decimal)acceptedOrders / DAILY_TARGET * 100;

        string status;
        string note;

        if (acceptedOrders >= DAILY_TARGET)
        {
            status = "✅ Target Achieved";
            if (difference > 5)
            {
                note = $"Excellent! {difference} orders above target";
            }
            else if (difference > 0)
            {
                note = $"Good! {difference} orders above target";
            }
            else
            {
                note = "Target exactly met";
            }
        }
        else
        {
            status = "❌ Target Not Met";
            note = $"Short by {Math.Abs(difference)} orders";
        }

        return new TargetAnalysis(
            Achieved: acceptedOrders >= DAILY_TARGET,
            Difference: difference,
            Percentage: Math.Round(percentage, 2),
            Status: status,
            Note: note
        );
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
        mapping.ShiftDateColumn = FindColumn(headerCells, HungerExcelColumns.ShiftDateColumns);
        mapping.DaysColumn = FindColumn(headerCells, HungerExcelColumns.DaysColumns);
        mapping.AcceptedOrdersColumn = FindColumn(headerCells, HungerExcelColumns.AcceptedOrdersColumns);

        var missingColumns = new List<string>();

        if (mapping.ActualWorkingIdColumn == 0)
            missingColumns.Add($"ActualWorkingId (tried: {string.Join(", ", HungerExcelColumns.ActualWorkingIdColumns)})");
        if (mapping.ShiftDateColumn == 0)
            missingColumns.Add($"ShiftDate (tried: {string.Join(", ", HungerExcelColumns.ShiftDateColumns)})");
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

    private static (
        bool IsValid,
        string? ActualWorkingId,
        DateOnly? ShiftDate,
        int? Days,
        int? AcceptedDailyOrders,
        string? ErrorMessage) ParseExcelRow(IXLRow row, ExcelColumnMapping mapping, int rowNumber)
    {
        try
        {
            // Parse ActualWorkingId
            var workingIdCell = row.Cell(mapping.ActualWorkingIdColumn).Value;
            var workingId = workingIdCell.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(workingId))
                return (false, null, null, null, null, "Invalid Working ID");

            // Parse ShiftDate
            var dateCell = row.Cell(mapping.ShiftDateColumn).Value;
            DateOnly shiftDate;
            if (dateCell.IsDateTime)
            {
                shiftDate = DateOnly.FromDateTime(dateCell.GetDateTime());
            }
            else if (DateOnly.TryParse(dateCell.ToString(), out var parsedDate))
            {
                shiftDate = parsedDate;
            }
            else
            {
                return (false, workingId, null, null, null, "Invalid Shift Date format");
            }

            // Parse Days
            var daysCell = row.Cell(mapping.DaysColumn).Value;
            if (!int.TryParse(daysCell.ToString(), out var days) || days <= 0)
                return (false, workingId, shiftDate, null, null, "Invalid Days (must be > 0)");

            // Parse AcceptedOrders
            var ordersCell = row.Cell(mapping.AcceptedOrdersColumn).Value;
            if (!int.TryParse(ordersCell.ToString(), out var acceptedOrders) || acceptedOrders < 0)
                return (false, workingId, shiftDate, days, null, "Invalid Accepted Orders (must be >= 0)");

            return (true, workingId, shiftDate, days, acceptedOrders, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, null, null, $"Error parsing row: {ex.Message}");
        }
    }
}
