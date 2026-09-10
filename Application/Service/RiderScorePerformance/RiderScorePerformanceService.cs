using System.Globalization;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.RiderScorePerformance;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.RiderScorePerformance;

public class RiderScorePerformanceService(
    ApplicationDbcontext dbcontext,
    IRiderWorkingIdHistoryService riderWorkingIdHistoryService) : IRiderScorePerformanceService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;
    private readonly IRiderWorkingIdHistoryService riderWorkingIdHistoryService = riderWorkingIdHistoryService;

    public async Task<Result<RiderScorePerformanceResponse>> CreateAsync(
        CreateRiderScorePerformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateMetrics(
            request.TotalVerificationRequests,
            request.SuccessfulVerificationRequests,
            request.VerificationSuccessRate,
            request.GrossOrders,
            request.CompletedOrders,
            request.CompletedOrdersInTime,
            request.FailedOrdersByRider,
            request.OnTimeDeliveryScore,
            request.FinalDeliveryQualityScore,
            request.Segment);

        if (validationError is not null)
            return Result.Failure<RiderScorePerformanceResponse>(
                RiderScorePerformanceErrors.InvalidRequest(validationError));

        var sourceRiderId = request.RiderWorkingId?.Trim();
        if (string.IsNullOrWhiteSpace(sourceRiderId))
            return Result.Failure<RiderScorePerformanceResponse>(
                RiderScorePerformanceErrors.InvalidRequest("RiderWorkingId is required."));

        try
        {
            var resolution = await ResolveRiderAsync(sourceRiderId, cancellationToken);
            if (resolution.IsDeleted)
                return Result.Failure<RiderScorePerformanceResponse>(
                    RiderScorePerformanceErrors.DeletedRider(sourceRiderId));
            if (resolution.Rider is null)
                return Result.Failure<RiderScorePerformanceResponse>(
                    RiderScorePerformanceErrors.RiderNotFound(sourceRiderId));
            var rider = resolution.Rider;

            var duplicate = await dbcontext.RiderScorePerformances.AnyAsync(
                x => x.RiderId == rider.Id && x.PerformanceDate == request.PerformanceDate,
                cancellationToken);

            if (duplicate)
                return Result.Failure<RiderScorePerformanceResponse>(RiderScorePerformanceErrors.Duplicate);

            var entity = new Domain.Entities.RiderScorePerformance
            {
                RiderId = rider.Id,
                Rider = rider,
                WorkingId = sourceRiderId,
                SourceRiderId = sourceRiderId,
                SubstituteRiderName = resolution.SubstituteRiderName,
                PerformanceDate = request.PerformanceDate,
                TotalVerificationRequests = request.TotalVerificationRequests,
                SuccessfulVerificationRequests = request.SuccessfulVerificationRequests,
                VerificationSuccessRate = request.VerificationSuccessRate,
                GrossOrders = request.GrossOrders,
                CompletedOrders = request.CompletedOrders,
                CompletedOrdersInTime = request.CompletedOrdersInTime,
                FailedOrdersByRider = request.FailedOrdersByRider,
                OnTimeDeliveryScore = request.OnTimeDeliveryScore,
                FinalDeliveryQualityScore = request.FinalDeliveryQualityScore,
                Segment = request.Segment.Trim()
            };

            dbcontext.RiderScorePerformances.Add(entity);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToResponse(entity));
        }
        catch (DbUpdateException)
        {
            return Result.Failure<RiderScorePerformanceResponse>(RiderScorePerformanceErrors.Duplicate);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderScorePerformanceResponse>(
                RiderScorePerformanceErrors.ServerError(ex.Message));
        }
    }

    public async Task<Result<RiderScorePerformanceResponse>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var response = await ResponseQuery()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return response is null
            ? Result.Failure<RiderScorePerformanceResponse>(RiderScorePerformanceErrors.NotFound)
            : Result.Success(response);
    }

    public async Task<Result<RiderScorePerformanceListResponse>> ListAsync(
        RiderScorePerformanceFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (filter.StartDate.HasValue && filter.EndDate.HasValue && filter.StartDate > filter.EndDate)
            return Result.Failure<RiderScorePerformanceListResponse>(
                RiderScorePerformanceErrors.InvalidRequest("StartDate must be on or before EndDate."));

        var query = dbcontext.RiderScorePerformances.AsNoTracking().AsQueryable();

        if (filter.RiderId.HasValue)
            query = query.Where(x => x.RiderId == filter.RiderId.Value);
        if (!string.IsNullOrWhiteSpace(filter.WorkingId))
        {
            var workingId = filter.WorkingId.Trim();
            query = query.Where(x => x.WorkingId == workingId || x.SourceRiderId == workingId);
        }
        if (filter.Date.HasValue)
            query = query.Where(x => x.PerformanceDate == filter.Date.Value);
        if (filter.StartDate.HasValue)
            query = query.Where(x => x.PerformanceDate >= filter.StartDate.Value);
        if (filter.EndDate.HasValue)
            query = query.Where(x => x.PerformanceDate <= filter.EndDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Segment))
        {
            var segment = filter.Segment.Trim();
            query = query.Where(x => x.Segment == segment);
        }

        var records = await query
            .OrderBy(x => x.PerformanceDate)
            .ThenBy(x => x.WorkingId)
            .Select(x => new RiderScorePerformanceResponse(
                x.Id,
                x.RiderId,
                x.WorkingId,
                x.SourceRiderId,
                x.Rider.Employee.NameAR,
                x.SubstituteRiderName,
                x.PerformanceDate,
                x.TotalVerificationRequests,
                x.SuccessfulVerificationRequests,
                x.VerificationSuccessRate,
                x.GrossOrders,
                x.CompletedOrders,
                x.CompletedOrdersInTime,
                x.FailedOrdersByRider,
                x.OnTimeDeliveryScore,
                x.FinalDeliveryQualityScore,
                x.Segment,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        var groupedDays = records
            .GroupBy(x => x.PerformanceDate)
            .Select(x => new RiderScorePerformanceDay(
                x.Key,
                x.ToList(),
                CalculateTotals(x)))
            .ToList();
        var days = filter.StartDate.HasValue && filter.EndDate.HasValue
            ? BuildCompleteDateRange(filter.StartDate.Value, filter.EndDate.Value, groupedDays)
            : groupedDays;

        return Result.Success(new RiderScorePerformanceListResponse(
            filter.StartDate,
            filter.EndDate,
            days,
            CalculateTotals(records)));
    }

    public async Task<Result<RiderScorePerformanceResponse>> UpdateAsync(
        int id,
        UpdateRiderScorePerformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await dbcontext.RiderScorePerformances
                .Include(x => x.Rider)
                .ThenInclude(x => x.Employee)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (entity is null)
                return Result.Failure<RiderScorePerformanceResponse>(RiderScorePerformanceErrors.NotFound);

            var targetDate = request.PerformanceDate ?? entity.PerformanceDate;
            var rider = entity.Rider;
            var sourceRiderId = entity.SourceRiderId;
            var substituteRiderName = entity.SubstituteRiderName;

            if (request.RiderWorkingId is not null)
            {
                sourceRiderId = request.RiderWorkingId.Trim();
                if (sourceRiderId.Length == 0)
                    return Result.Failure<RiderScorePerformanceResponse>(
                        RiderScorePerformanceErrors.InvalidRequest("RiderWorkingId cannot be empty."));

                var resolution = await ResolveRiderAsync(sourceRiderId, cancellationToken);
                if (resolution.IsDeleted)
                    return Result.Failure<RiderScorePerformanceResponse>(
                        RiderScorePerformanceErrors.DeletedRider(sourceRiderId));
                if (resolution.Rider is null)
                    return Result.Failure<RiderScorePerformanceResponse>(
                        RiderScorePerformanceErrors.RiderNotFound(sourceRiderId));
                rider = resolution.Rider;
                substituteRiderName = resolution.SubstituteRiderName;
            }

            var validationError = ValidateMetrics(
                request.TotalVerificationRequests ?? entity.TotalVerificationRequests,
                request.SuccessfulVerificationRequests ?? entity.SuccessfulVerificationRequests,
                request.VerificationSuccessRate ?? entity.VerificationSuccessRate,
                request.GrossOrders ?? entity.GrossOrders,
                request.CompletedOrders ?? entity.CompletedOrders,
                request.CompletedOrdersInTime ?? entity.CompletedOrdersInTime,
                request.FailedOrdersByRider ?? entity.FailedOrdersByRider,
                request.OnTimeDeliveryScore ?? entity.OnTimeDeliveryScore,
                request.FinalDeliveryQualityScore ?? entity.FinalDeliveryQualityScore,
                request.Segment ?? entity.Segment);

            if (validationError is not null)
                return Result.Failure<RiderScorePerformanceResponse>(
                    RiderScorePerformanceErrors.InvalidRequest(validationError));

            if (rider.Id != entity.RiderId || targetDate != entity.PerformanceDate)
            {
                var duplicate = await dbcontext.RiderScorePerformances.AnyAsync(
                    x => x.Id != id && x.RiderId == rider.Id && x.PerformanceDate == targetDate,
                    cancellationToken);
                if (duplicate)
                    return Result.Failure<RiderScorePerformanceResponse>(RiderScorePerformanceErrors.Duplicate);
            }

            entity.RiderId = rider.Id;
            entity.Rider = rider;
            entity.WorkingId = sourceRiderId;
            entity.SourceRiderId = sourceRiderId;
            entity.SubstituteRiderName = substituteRiderName;
            entity.PerformanceDate = targetDate;
            entity.TotalVerificationRequests = request.TotalVerificationRequests ?? entity.TotalVerificationRequests;
            entity.SuccessfulVerificationRequests = request.SuccessfulVerificationRequests ?? entity.SuccessfulVerificationRequests;
            entity.VerificationSuccessRate = request.VerificationSuccessRate ?? entity.VerificationSuccessRate;
            entity.GrossOrders = request.GrossOrders ?? entity.GrossOrders;
            entity.CompletedOrders = request.CompletedOrders ?? entity.CompletedOrders;
            entity.CompletedOrdersInTime = request.CompletedOrdersInTime ?? entity.CompletedOrdersInTime;
            entity.FailedOrdersByRider = request.FailedOrdersByRider ?? entity.FailedOrdersByRider;
            entity.OnTimeDeliveryScore = request.OnTimeDeliveryScore ?? entity.OnTimeDeliveryScore;
            entity.FinalDeliveryQualityScore = request.FinalDeliveryQualityScore ?? entity.FinalDeliveryQualityScore;
            entity.Segment = (request.Segment ?? entity.Segment).Trim();
            entity.UpdatedAt = DateTime.UtcNow.AddHours(3);

            await dbcontext.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToResponse(entity));
        }
        catch (DbUpdateException)
        {
            return Result.Failure<RiderScorePerformanceResponse>(RiderScorePerformanceErrors.Duplicate);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderScorePerformanceResponse>(
                RiderScorePerformanceErrors.ServerError(ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbcontext.RiderScorePerformances
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return Result.Failure(RiderScorePerformanceErrors.NotFound);

        dbcontext.RiderScorePerformances.Remove(entity);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<RiderScorePerformanceImportResult>> ImportAsync(
        Stream excelStream,
        DateOnly performanceDate,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<RiderScorePerformanceImportError>();

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheet(1);
            var mapping = FindColumnIndices(worksheet);

            if (!mapping.IsValid)
                return Result.Failure<RiderScorePerformanceImportResult>(
                    RiderScorePerformanceErrors.InvalidExcel(mapping.ErrorMessage!));

            var rows = worksheet.RowsUsed().Skip(1).ToList();
            var recordsToAdd = new List<Domain.Entities.RiderScorePerformance>();
            var riderIdsInFile = new HashSet<int>();
            var periodStart = new DateOnly(performanceDate.Year, performanceDate.Month, 1);
            var existingRiderIds = await dbcontext.RiderScorePerformances
                .AsNoTracking()
                .Where(x => x.PerformanceDate == performanceDate)
                .Select(x => x.RiderId)
                .ToHashSetAsync(cancellationToken);
            var priorDailyTotals = await dbcontext.RiderScorePerformances
                .AsNoTracking()
                .Where(x => x.PerformanceDate >= periodStart && x.PerformanceDate < performanceDate)
                .GroupBy(x => x.RiderId)
                .Select(x => new CumulativePerformanceBaseline(
                    x.Key,
                    x.Sum(y => y.TotalVerificationRequests),
                    x.Sum(y => y.SuccessfulVerificationRequests),
                    x.Sum(y => y.GrossOrders),
                    x.Sum(y => y.CompletedOrders),
                    x.Sum(y => y.CompletedOrdersInTime),
                    x.Sum(y => y.FailedOrdersByRider)))
                .ToDictionaryAsync(x => x.RiderId, cancellationToken);

            var rowNumber = 1;
            foreach (var row in rows)
            {
                rowNumber++;
                var parsed = ParseRow(row, mapping);
                if (!parsed.IsValid)
                {
                    errors.Add(new RiderScorePerformanceImportError(
                        rowNumber,
                        parsed.RiderWorkingId ?? "N/A",
                        parsed.ErrorMessage!));
                    continue;
                }

                var data = parsed.Data!;
                var resolution = await ResolveRiderAsync(data.RiderWorkingId, cancellationToken);
                if (resolution.IsDeleted)
                {
                    errors.Add(new RiderScorePerformanceImportError(
                        rowNumber,
                        data.RiderWorkingId,
                        $"WorkingId {data.RiderWorkingId} belongs to a deleted employee. Performance cannot be imported."));
                    continue;
                }
                if (resolution.Rider is null)
                {
                    errors.Add(new RiderScorePerformanceImportError(
                        rowNumber,
                        data.RiderWorkingId,
                        $"No rider found with working ID {data.RiderWorkingId}"));
                    continue;
                }
                var rider = resolution.Rider;

                if (!riderIdsInFile.Add(rider.Id))
                {
                    errors.Add(new RiderScorePerformanceImportError(
                        rowNumber,
                        data.RiderWorkingId,
                        "Duplicate daily performance record in the Excel file for this rider."));
                    continue;
                }

                if (existingRiderIds.Contains(rider.Id))
                {
                    errors.Add(new RiderScorePerformanceImportError(
                        rowNumber,
                        data.RiderWorkingId,
                        $"A daily performance record already exists for this rider on {performanceDate:yyyy-MM-dd}."));
                    continue;
                }

                var baseline = priorDailyTotals.GetValueOrDefault(rider.Id);
                var dailyCounters = GetDailyCounters(data, baseline);
                if (!dailyCounters.IsValid)
                {
                    errors.Add(new RiderScorePerformanceImportError(
                        rowNumber,
                        data.RiderWorkingId,
                        dailyCounters.ErrorMessage!));
                    continue;
                }

                recordsToAdd.Add(new Domain.Entities.RiderScorePerformance
                {
                    RiderId = rider.Id,
                    Rider = rider,
                    WorkingId = data.RiderWorkingId,
                    SourceRiderId = data.RiderWorkingId,
                    SubstituteRiderName = resolution.SubstituteRiderName,
                    PerformanceDate = performanceDate,
                    TotalVerificationRequests = dailyCounters.TotalVerificationRequests,
                    SuccessfulVerificationRequests = dailyCounters.SuccessfulVerificationRequests,
                    VerificationSuccessRate = data.VerificationSuccessRate,
                    GrossOrders = dailyCounters.GrossOrders,
                    CompletedOrders = dailyCounters.CompletedOrders,
                    CompletedOrdersInTime = dailyCounters.CompletedOrdersInTime,
                    FailedOrdersByRider = dailyCounters.FailedOrdersByRider,
                    OnTimeDeliveryScore = data.OnTimeDeliveryScore,
                    FinalDeliveryQualityScore = data.FinalDeliveryQualityScore,
                    Segment = data.Segment
                });
            }

            if (recordsToAdd.Count > 0)
            {
                dbcontext.RiderScorePerformances.AddRange(recordsToAdd);
                await dbcontext.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(new RiderScorePerformanceImportResult(
                rows.Count,
                recordsToAdd.Count,
                errors.Count,
                errors));
        }
        catch (DbUpdateException)
        {
            return Result.Failure<RiderScorePerformanceImportResult>(RiderScorePerformanceErrors.Duplicate);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderScorePerformanceImportResult>(
                RiderScorePerformanceErrors.InvalidExcel($"The Excel file could not be processed: {ex.Message}"));
        }
    }

    private IQueryable<RiderScorePerformanceResponse> ResponseQuery() =>
        dbcontext.RiderScorePerformances
            .AsNoTracking()
            .Select(x => new RiderScorePerformanceResponse(
                x.Id,
                x.RiderId,
                x.WorkingId,
                x.SourceRiderId,
                x.Rider.Employee.NameAR,
                x.SubstituteRiderName,
                x.PerformanceDate,  
                x.TotalVerificationRequests,
                x.SuccessfulVerificationRequests,
                x.VerificationSuccessRate,
                x.GrossOrders,
                x.CompletedOrders,
                x.CompletedOrdersInTime,
                x.FailedOrdersByRider,
                x.OnTimeDeliveryScore,
                x.FinalDeliveryQualityScore,
                x.Segment,
                x.CreatedAt,
                x.UpdatedAt));

    private static RiderScorePerformanceResponse MapToResponse(Domain.Entities.RiderScorePerformance entity) => new(
        entity.Id,
        entity.RiderId,
        entity.WorkingId,
        entity.SourceRiderId,
        entity.Rider.Employee.NameAR,
        entity.SubstituteRiderName,
        entity.PerformanceDate,
        entity.TotalVerificationRequests,
        entity.SuccessfulVerificationRequests,
        entity.VerificationSuccessRate,
        entity.GrossOrders,
        entity.CompletedOrders,
        entity.CompletedOrdersInTime,
        entity.FailedOrdersByRider,
        entity.OnTimeDeliveryScore,
        entity.FinalDeliveryQualityScore,
        entity.Segment,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static RiderScorePerformanceTotals CalculateTotals(
        IEnumerable<RiderScorePerformanceResponse> records)
    {
        var list = records.ToList();
        var totalVerificationRequests = list.Sum(x => x.TotalVerificationRequests);
        var successfulVerificationRequests = list.Sum(x => x.SuccessfulVerificationRequests);

        return new RiderScorePerformanceTotals(
            list.Count,
            list.Select(x => x.RiderId).Distinct().Count(),
            totalVerificationRequests,
            successfulVerificationRequests,
            totalVerificationRequests == 0
                ? 0
                : decimal.Round((decimal)successfulVerificationRequests / totalVerificationRequests, 4),
            list.Sum(x => x.GrossOrders),
            list.Sum(x => x.CompletedOrders),
            list.Sum(x => x.CompletedOrdersInTime),
            list.Sum(x => x.FailedOrdersByRider),
            list.Count == 0 ? 0 : decimal.Round(list.Average(x => x.OnTimeDeliveryScore), 4),
            list.Count == 0 ? 0 : decimal.Round(list.Average(x => x.FinalDeliveryQualityScore), 4));
    }

    private static IReadOnlyList<RiderScorePerformanceDay> BuildCompleteDateRange(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<RiderScorePerformanceDay> groupedDays)
    {
        var daysByDate = groupedDays.ToDictionary(x => x.PerformanceDate);
        var emptyTotals = CalculateTotals([]);
        var days = new List<RiderScorePerformanceDay>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            days.Add(daysByDate.GetValueOrDefault(date) ?? new RiderScorePerformanceDay(
                date,
                [],
                emptyTotals));
        }

        return days;
    }

    private async Task<RiderResolution> ResolveRiderAsync(
        string workingId,
        CancellationToken cancellationToken)
    {
        var substitution = await dbcontext.RiderShiftSubstitutions
            .Include(x => x.ActualRider)
            .ThenInclude(x => x!.Employee)
            .Include(x => x.SubstituteRider)
            .ThenInclude(x => x.Employee)
            .FirstOrDefaultAsync(
                x => x.ActualRiderWorkingId == workingId && x.IsActive,
                cancellationToken);

        var substituteRiderName = substitution is null
            ? null
            : GetRiderName(substitution.SubstituteRider);

        if (substitution?.ActualRider is not null)
            return new RiderResolution(substitution.ActualRider, false, substituteRiderName);

        var rider = await dbcontext.RiderDetails
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.WorkingId == workingId, cancellationToken);

        if (rider is not null)
            return new RiderResolution(rider, false, substituteRiderName);

        var history = await riderWorkingIdHistoryService.WhoHasWorkingId(workingId, cancellationToken);
        if (history.IsSuccess && history.Value.IsCurrentlyAssigned)
        {
            var currentRider = await riderWorkingIdHistoryService.GetRiderByWorkingId(
                workingId,
                cancellationToken);

            if (currentRider.IsSuccess && currentRider.Value is not null)
                return new RiderResolution(currentRider.Value, false, substituteRiderName);
        }

        var isDeleted = await dbcontext.DeletedEmployees
            .AsNoTracking()
            .AnyAsync(x => x.WorkingId == workingId, cancellationToken);

        return new RiderResolution(null, isDeleted, substituteRiderName);
    }

    private static string GetRiderName(RiderDetails rider) =>
        !string.IsNullOrWhiteSpace(rider.Employee.NameAR)
            ? rider.Employee.NameAR
            : rider.Employee.NameEN;

    private static string? ValidateMetrics(
        int totalVerificationRequests,
        int successfulVerificationRequests,
        decimal verificationSuccessRate,
        int grossOrders,
        int completedOrders,
        int completedOrdersInTime,
        int failedOrdersByRider,
        decimal onTimeDeliveryScore,
        decimal finalDeliveryQualityScore,
        string? segment)
    {
        if (totalVerificationRequests < 0 || successfulVerificationRequests < 0 || grossOrders < 0 ||
            completedOrders < 0 || completedOrdersInTime < 0 || failedOrdersByRider < 0)
            return "Request and order counts must be zero or greater.";
        if (verificationSuccessRate is < 0 or > 1)
            return "VerificationSuccessRate must be between 0 and 1.";
        if (onTimeDeliveryScore is < 0 or > 1)
            return "OnTimeDeliveryScore must be between 0 and 1.";
        if (finalDeliveryQualityScore is < 0 or > 1)
            return "FinalDeliveryQualityScore must be between 0 and 1.";
        if (string.IsNullOrWhiteSpace(segment))
            return "Segment is required.";
        if (segment.Trim().Length > 50)
            return "Segment cannot exceed 50 characters.";
        return null;
    }

    private static PerformanceColumnMapping FindColumnIndices(IXLWorksheet worksheet)
    {
        var headerRow = worksheet.FirstRowUsed();
        if (headerRow is null)
            return PerformanceColumnMapping.Invalid("Excel file is empty or has no header row.");

        var cells = headerRow.CellsUsed().ToList();
        var mapping = new PerformanceColumnMapping
        {
            RiderIdColumn = FindColumn(cells, PerformanceExcelColumns.RiderId),
            TotalVerificationRequestsColumn = FindColumn(cells, PerformanceExcelColumns.TotalVerificationRequests),
            SuccessfulVerificationRequestsColumn = FindColumn(cells, PerformanceExcelColumns.SuccessfulVerificationRequests),
            VerificationSuccessRateColumn = FindColumn(cells, PerformanceExcelColumns.VerificationSuccessRate),
            GrossOrdersColumn = FindColumn(cells, PerformanceExcelColumns.GrossOrders),
            CompletedOrdersColumn = FindColumn(cells, PerformanceExcelColumns.CompletedOrders),
            CompletedOrdersInTimeColumn = FindColumn(cells, PerformanceExcelColumns.CompletedOrdersInTime),
            FailedOrdersByRiderColumn = FindColumn(cells, PerformanceExcelColumns.FailedOrdersByRider),
            OnTimeDeliveryScoreColumn = FindColumn(cells, PerformanceExcelColumns.OnTimeDeliveryScore),
            FinalDeliveryQualityScoreColumn = FindColumn(cells, PerformanceExcelColumns.FinalDeliveryQualityScore),
            SegmentColumn = FindColumn(cells, PerformanceExcelColumns.Segment)
        };

        var missing = new List<string>();
        AddMissing(mapping.RiderIdColumn, "rider_id", PerformanceExcelColumns.RiderId, missing);
        AddMissing(mapping.TotalVerificationRequestsColumn, "total_verification_requests", PerformanceExcelColumns.TotalVerificationRequests, missing);
        AddMissing(mapping.SuccessfulVerificationRequestsColumn, "successful_verification_requests", PerformanceExcelColumns.SuccessfulVerificationRequests, missing);
        AddMissing(mapping.VerificationSuccessRateColumn, "verification_success_rate", PerformanceExcelColumns.VerificationSuccessRate, missing);
        AddMissing(mapping.GrossOrdersColumn, "gross_orders", PerformanceExcelColumns.GrossOrders, missing);
        AddMissing(mapping.CompletedOrdersColumn, "completed_orders", PerformanceExcelColumns.CompletedOrders, missing);
        AddMissing(mapping.CompletedOrdersInTimeColumn, "completed_orders_in_time", PerformanceExcelColumns.CompletedOrdersInTime, missing);
        AddMissing(mapping.FailedOrdersByRiderColumn, "failed_orders_by_rider", PerformanceExcelColumns.FailedOrdersByRider, missing);
        AddMissing(mapping.OnTimeDeliveryScoreColumn, "on_time_delivery_score", PerformanceExcelColumns.OnTimeDeliveryScore, missing);
        AddMissing(mapping.FinalDeliveryQualityScoreColumn, "final_delivery_quality_score", PerformanceExcelColumns.FinalDeliveryQualityScore, missing);
        AddMissing(mapping.SegmentColumn, "segment", PerformanceExcelColumns.Segment, missing);

        if (missing.Count > 0)
            return PerformanceColumnMapping.Invalid($"Missing required columns: {string.Join("; ", missing)}");

        mapping.IsValid = true;
        return mapping;
    }

    private static void AddMissing(int column, string canonicalName, string[] aliases, List<string> missing)
    {
        if (column == 0)
            missing.Add($"{canonicalName} (tried: {string.Join(", ", aliases)})");
    }

    private static int FindColumn(IEnumerable<IXLCell> cells, IEnumerable<string> names)
    {
        foreach (var cell in cells)
        {
            var value = cell.Value.ToString().Trim();
            if (names.Any(name => value.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return cell.Address.ColumnNumber;
        }

        return 0;
    }

    private static ParsedPerformanceRow ParseRow(IXLRow row, PerformanceColumnMapping mapping)
    {
        var riderWorkingId = row.Cell(mapping.RiderIdColumn).Value.ToString().Trim();
        if (string.IsNullOrWhiteSpace(riderWorkingId))
            return ParsedPerformanceRow.Invalid(null, "Invalid rider_id.");

        if (!TryReadNonNegativeInt(row.Cell(mapping.TotalVerificationRequestsColumn), out var totalVerificationRequests))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid total_verification_requests (must be >= 0).");
        if (!TryReadNonNegativeInt(row.Cell(mapping.SuccessfulVerificationRequestsColumn), out var successfulVerificationRequests))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid successful_verification_requests (must be >= 0).");
        if (!TryReadRate(row.Cell(mapping.VerificationSuccessRateColumn), out var verificationSuccessRate))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid verification_success_rate (must be between 0 and 1).");
        if (!TryReadNonNegativeInt(row.Cell(mapping.GrossOrdersColumn), out var grossOrders))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid gross_orders (must be >= 0).");
        if (!TryReadNonNegativeInt(row.Cell(mapping.CompletedOrdersColumn), out var completedOrders))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid completed_orders (must be >= 0).");
        if (!TryReadNonNegativeInt(row.Cell(mapping.CompletedOrdersInTimeColumn), out var completedOrdersInTime))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid completed_orders_in_time (must be >= 0).");
        if (!TryReadNonNegativeInt(row.Cell(mapping.FailedOrdersByRiderColumn), out var failedOrdersByRider))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid failed_orders_by_rider (must be >= 0).");
        if (!TryReadRate(row.Cell(mapping.OnTimeDeliveryScoreColumn), out var onTimeDeliveryScore))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid on_time_delivery_score (must be between 0 and 1).");
        if (!TryReadRate(row.Cell(mapping.FinalDeliveryQualityScoreColumn), out var finalDeliveryQualityScore))
            return ParsedPerformanceRow.Invalid(riderWorkingId, "Invalid final_delivery_quality_score (must be between 0 and 1).");

        var segment = row.Cell(mapping.SegmentColumn).Value.ToString().Trim();
        var validationError = ValidateMetrics(
            totalVerificationRequests,
            successfulVerificationRequests,
            verificationSuccessRate,
            grossOrders,
            completedOrders,
            completedOrdersInTime,
            failedOrdersByRider,
            onTimeDeliveryScore,
            finalDeliveryQualityScore,
            segment);

        if (validationError is not null)
            return ParsedPerformanceRow.Invalid(riderWorkingId, validationError);

        return ParsedPerformanceRow.Valid(new PerformanceRowData(
            riderWorkingId,
            totalVerificationRequests,
            successfulVerificationRequests,
            verificationSuccessRate,
            grossOrders,
            completedOrders,
            completedOrdersInTime,
            failedOrdersByRider,
            onTimeDeliveryScore,
            finalDeliveryQualityScore,
            segment));
    }

    private static bool TryReadNonNegativeInt(IXLCell cell, out int value)
    {
        if (cell.TryGetValue(out value) && value >= 0)
            return true;

        return int.TryParse(
                   cell.Value.ToString().Trim(),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value) && value >= 0;
    }

    private static bool TryReadRate(IXLCell cell, out decimal value)
    {
        if (!cell.TryGetValue(out value) &&
            !decimal.TryParse(
                cell.Value.ToString().Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value))
            return false;

        return value is >= 0 and <= 1;
    }

    private static DailyPerformanceCounters GetDailyCounters(
        PerformanceRowData uploadedTotals,
        CumulativePerformanceBaseline? baseline)
    {
        var prior = baseline ?? CumulativePerformanceBaseline.Empty;

        if (uploadedTotals.TotalVerificationRequests < prior.TotalVerificationRequests ||
            uploadedTotals.SuccessfulVerificationRequests < prior.SuccessfulVerificationRequests ||
            uploadedTotals.GrossOrders < prior.GrossOrders ||
            uploadedTotals.CompletedOrders < prior.CompletedOrders ||
            uploadedTotals.CompletedOrdersInTime < prior.CompletedOrdersInTime ||
            uploadedTotals.FailedOrdersByRider < prior.FailedOrdersByRider)
        {
            return DailyPerformanceCounters.Invalid(
                "Cumulative totals are lower than the records already saved for this rider in the current month. " +
                "The row was skipped to avoid creating negative daily performance values.");
        }

        return DailyPerformanceCounters.Valid(
            uploadedTotals.TotalVerificationRequests - prior.TotalVerificationRequests,
            uploadedTotals.SuccessfulVerificationRequests - prior.SuccessfulVerificationRequests,
            uploadedTotals.GrossOrders - prior.GrossOrders,
            uploadedTotals.CompletedOrders - prior.CompletedOrders,
            uploadedTotals.CompletedOrdersInTime - prior.CompletedOrdersInTime,
            uploadedTotals.FailedOrdersByRider - prior.FailedOrdersByRider);
    }

    private sealed class PerformanceColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int RiderIdColumn { get; set; }
        public int TotalVerificationRequestsColumn { get; set; }
        public int SuccessfulVerificationRequestsColumn { get; set; }
        public int VerificationSuccessRateColumn { get; set; }
        public int GrossOrdersColumn { get; set; }
        public int CompletedOrdersColumn { get; set; }
        public int CompletedOrdersInTimeColumn { get; set; }
        public int FailedOrdersByRiderColumn { get; set; }
        public int OnTimeDeliveryScoreColumn { get; set; }
        public int FinalDeliveryQualityScoreColumn { get; set; }
        public int SegmentColumn { get; set; }

        public static PerformanceColumnMapping Invalid(string message) => new()
        {
            ErrorMessage = message
        };
    }

    private sealed record PerformanceRowData(
        string RiderWorkingId,
        int TotalVerificationRequests,
        int SuccessfulVerificationRequests,
        decimal VerificationSuccessRate,
        int GrossOrders,
        int CompletedOrders,
        int CompletedOrdersInTime,
        int FailedOrdersByRider,
        decimal OnTimeDeliveryScore,
        decimal FinalDeliveryQualityScore,
        string Segment);

    private sealed record CumulativePerformanceBaseline(
        int RiderId,
        int TotalVerificationRequests,
        int SuccessfulVerificationRequests,
        int GrossOrders,
        int CompletedOrders,
        int CompletedOrdersInTime,
        int FailedOrdersByRider)
    {
        public static readonly CumulativePerformanceBaseline Empty = new(0, 0, 0, 0, 0, 0, 0);
    }

    private sealed record DailyPerformanceCounters(
        bool IsValid,
        int TotalVerificationRequests,
        int SuccessfulVerificationRequests,
        int GrossOrders,
        int CompletedOrders,
        int CompletedOrdersInTime,
        int FailedOrdersByRider,
        string? ErrorMessage)
    {
        public static DailyPerformanceCounters Valid(
            int totalVerificationRequests,
            int successfulVerificationRequests,
            int grossOrders,
            int completedOrders,
            int completedOrdersInTime,
            int failedOrdersByRider) => new(
                true,
                totalVerificationRequests,
                successfulVerificationRequests,
                grossOrders,
                completedOrders,
                completedOrdersInTime,
                failedOrdersByRider,
                null);

        public static DailyPerformanceCounters Invalid(string message) =>
            new(false, 0, 0, 0, 0, 0, 0, message);
    }

    private sealed record RiderResolution(
        RiderDetails? Rider,
        bool IsDeleted,
        string? SubstituteRiderName);

    private sealed record ParsedPerformanceRow(
        bool IsValid,
        string? RiderWorkingId,
        PerformanceRowData? Data,
        string? ErrorMessage)
    {
        public static ParsedPerformanceRow Valid(PerformanceRowData data) =>
            new(true, data.RiderWorkingId, data, null);

        public static ParsedPerformanceRow Invalid(string? riderWorkingId, string message) =>
            new(false, riderWorkingId, null, message);
    }

    private static class PerformanceExcelColumns
    {
        public static readonly string[] RiderId =
            ["rider_id", "Rider Id", "Rider ID", "Rider_ID", "RiderID", "Working_ID", "Working ID", "ID", "EmployeeID", "معرّف السائق"];
        public static readonly string[] TotalVerificationRequests =
            ["total_verification_requests", "Total Verification Requests", "TotalVerificationRequests"];
        public static readonly string[] SuccessfulVerificationRequests =
            ["successful_verification_requests", "Successful Verification Requests", "SuccessfulVerificationRequests"];
        public static readonly string[] VerificationSuccessRate =
            ["verification_success_rate", "Verification Success Rate", "VerificationSuccessRate"];
        public static readonly string[] GrossOrders =
            ["gross_orders", "Gross Orders", "GrossOrders"];
        public static readonly string[] CompletedOrders =
            ["completed_orders", "Completed Orders", "CompletedOrders"];
        public static readonly string[] CompletedOrdersInTime =
            ["completed_orders_in_time", "Completed Orders In Time", "Completed Orders in Time", "CompletedOrdersInTime"];
        public static readonly string[] FailedOrdersByRider =
            ["failed_orders_by_rider", "Failed Orders By Rider", "Failed Orders by Rider", "FailedOrdersByRider"];
        public static readonly string[] OnTimeDeliveryScore =
            ["on_time_delivery_score", "On Time Delivery Score", "OnTimeDeliveryScore"];
        public static readonly string[] FinalDeliveryQualityScore =
            ["final_delivery_quality_score", "Final Delivery Quality Score", "FinalDeliveryQualityScore"];
        public static readonly string[] Segment =
            ["segment", "Segment", "rider_segment", "Rider Segment"];
    }
}
