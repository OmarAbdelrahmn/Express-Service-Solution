using Application.Abstraction;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Office2010.Word;
using DocumentFormat.OpenXml.Spreadsheet;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;


namespace Application.Service.Riders;

public class RiderShiftService(ApplicationDbcontext dbcontext) : IRiderShiftService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;


    public async Task<Result<BulkImportResult>> ImportShiftsFromExcelAsync(
        Stream excelStream,
        DateOnly shiftDate,
        int rejectionThreshold = 2,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ImportError>();
        var conflicts = new List<ShiftConflictDto>();
        var successCount = 0;
        var totalRecords = 0;

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheet(1);

            var columnMapping = FindColumnIndices(worksheet);
            if (!columnMapping.IsValid)
            {
                return Result.Failure<BulkImportResult>(
                    new Error("InvalidExcel", columnMapping.ErrorMessage!, 400));
            }

            var rows = worksheet.RowsUsed().Skip(1);
            totalRecords = rows.Count();

            // Load active substitutions
            var activeSubstitutions = await dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Company)
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var substitutionDict = activeSubstitutions
                .ToDictionary(s => s.SubstituteWorkingId, s => s);

            // Load all riders
            var allRiderDetails = await dbcontext.RiderDetails
                .Include(r => r.Company)
                .Include(r => r.Employee)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ridersByWorkingId = allRiderDetails
                .Where(r => r.WorkingId.HasValue)
                .ToDictionary(r => r.WorkingId!.Value, r => r);

            var shiftsToAdd = new List<RiderShift>();
            var rowNumber = 1;

            foreach (var row in rows)
            {
                rowNumber++;
                try
                {
                    var shiftData = ParseExcelRowByName(row, columnMapping, rowNumber);

                    if (!shiftData.IsValid)
                    {
                        errors.Add(new ImportError(rowNumber, shiftData.WorkingId?.ToString() ?? "N/A", shiftData.ErrorMessage!));
                        continue;
                    }

                    RiderDetails? actualRider = null;
                    if (substitutionDict.TryGetValue(shiftData.WorkingId!.Value, out var substitution))
                        actualRider = substitution.ActualRider;
                    else if (ridersByWorkingId.TryGetValue(shiftData.WorkingId!.Value, out var rider))
                        actualRider = rider;

                    if (actualRider is null)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"No rider found with working ID {shiftData.WorkingId}"));
                        continue;
                    }

                    // Check duplicates in the Excel batch
                    if (shiftsToAdd.Any(s => s.RiderId == actualRider.Id &&
                                              s.WorkingId == shiftData.WorkingId &&
                                              s.ShiftDate == shiftDate))
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"Duplicate shift in Excel file for Working ID {shiftData.WorkingId}"));
                        continue;
                    }

                    // Create RiderShift object
                    var shiftStatus = CalculateShiftStatus(
                        shiftData.AcceptedDailyOrders!.Value,
                        actualRider.Company.Name);

                    var realRejectedOrders = Math.Max(0, shiftData.RejectedDailyOrders!.Value - rejectionThreshold);
                    var hasRejectionProblem = realRejectedOrders > rejectionThreshold;
                    var penaltyAmount = CalculateRejectionPenalty(realRejectedOrders);

                    var shift = new RiderShift
                    {
                        RiderId = actualRider.Id,
                        WorkingId = shiftData.WorkingId!.Value,
                        ShiftDate = shiftDate,
                        AcceptedDailyOrders = shiftData.AcceptedDailyOrders!.Value,
                        RejectedDailyOrders = shiftData.RejectedDailyOrders!.Value,
                        RealRejectedDailyOrders = realRejectedOrders,
                        StackedDeliveries = shiftData.StackedDeliveries!.Value,
                        WorkingHours = shiftData.WorkingHours!.Value,
                        CompanyId = actualRider.CompanyId,
                        ShiftStatus = shiftStatus,
                        CreatedAt = DateTime.UtcNow,
                        Rider = null
                    };

                    shiftsToAdd.Add(shift);

                    if (hasRejectionProblem)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"WARNING: Shift has {realRejectedOrders} rejections (exceeds threshold of {rejectionThreshold}). Penalty: {penaltyAmount} SAR"));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError(
                        rowNumber,
                        "N/A",
                        $"Error parsing row: {ex.Message}"));
                }
            }

            if (shiftsToAdd.Any())
            {
                // ✅ Load existing shifts from DB for the target date
                var existingDbShifts = await dbcontext.RiderShifts
                    .Where(rs => rs.ShiftDate == shiftDate)
                    .Include(r => r.Rider)
                        .ThenInclude(r => r.Company)
                    .Include(r => r.Rider)
                        .ThenInclude(r => r.Employee)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                // Check for conflicts
                foreach (var existing in existingDbShifts)
                {
                    var newShift = shiftsToAdd.FirstOrDefault(s =>
                        s.RiderId == existing.RiderId &&
                        s.WorkingId == existing.WorkingId &&
                        s.ShiftDate == existing.ShiftDate);

 

   

                    if (newShift is null) continue;

                    var conflict = new ShiftConflictDto
                    {
                        RiderId = existing.RiderId,
                        WorkingId = existing.WorkingId,
                        ShiftDate = existing.ShiftDate,
                        RowNumber = shiftsToAdd.IndexOf(newShift) + 2, 
                        RiderName = existing.Rider?.Employee?.NameAR ?? "Unknown",
                        CompanyName = existing.Rider?.Company?.Name ?? "Unknown",
                        ExistingShift = new ShiftDto
                        {
                            AcceptedOrders = existing.AcceptedDailyOrders,
                            RejectedOrders = existing.RejectedDailyOrders,
                            RealRejectedOrders = existing.RealRejectedDailyOrders,
                            WorkingHours = existing.WorkingHours,
                            ShiftStatus = existing.ShiftStatus,
                            CreatedAt = existing.CreatedAt
                        },
                        NewShift = new ShiftDto
                        {
                            AcceptedOrders = newShift.AcceptedDailyOrders,
                            RejectedOrders = newShift.RejectedDailyOrders,
                            RealRejectedOrders = newShift.RealRejectedDailyOrders,
                            WorkingHours = newShift.WorkingHours,
                            ShiftStatus = newShift.ShiftStatus,
                            CreatedAt = DateTime.UtcNow
                        }
                    };
                    conflicts.Add(conflict);
                }

                // Remove conflicting shifts
                var conflictKeys = conflicts
                    .Select(c => new { c.RiderId, c.WorkingId, c.ShiftDate })
                    .ToHashSet();

                shiftsToAdd = shiftsToAdd
                    .Where(s => !conflictKeys.Contains(new { s.RiderId, s.WorkingId, s.ShiftDate }))
                    .ToList();

                // Import non-conflicting shifts
                if (shiftsToAdd.Any() && !conflicts.Any())
                {
                    using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        await dbcontext.RiderShifts.AddRangeAsync(shiftsToAdd, cancellationToken);
                        await dbcontext.SaveChangesAsync(cancellationToken);
                        successCount = shiftsToAdd.Count;
                        await transaction.CommitAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Result.Failure<BulkImportResult>(
                            new Error("ServerError", $"Database error during bulk insert: {ex.Message}", 500));
                    }
                }
            }

            var result = new BulkImportResult(
                totalRecords,
                successCount,
                errors.Count,
                errors,
                conflicts.Count,
                conflicts
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkImportResult>(
                new Error("ServerError", $"Error reading Excel file: {ex.Message}", 500));
        }



    }

    public async Task<Result<BulkComparisonResult>> CreateShiftComparisonsAsync(
        Stream excelStream,
        DateOnly shiftDate,
        int rejectionThreshold = 2,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ImportError>();
        var comparisons = new List<ShiftComparisonResponse>();
        var newShiftCount = 0;
        var updateCount = 0;

        try
        {
            using var workbook = new XLWorkbook(excelStream);
            var worksheet = workbook.Worksheet(1);

            var columnMapping = FindColumnIndices(worksheet);
            if (!columnMapping.IsValid)
            {
                return Result.Failure<BulkComparisonResult>(
                    new Error("InvalidExcel", columnMapping.ErrorMessage!, 400));
            }

            var activeSubstitutions = await dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Company)
                .Include(s => s.ActualRider)
                    .ThenInclude(r => r.Employee)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var substitutionDict = activeSubstitutions
                .ToDictionary(s => s.SubstituteWorkingId, s => s);

            var allRiderDetails = await dbcontext.RiderDetails
                .Include(r => r.Company)
                .Include(r => r.Employee)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ridersByWorkingId = allRiderDetails
                .Where(r => r.WorkingId.HasValue)
                .ToDictionary(r => r.WorkingId!.Value, r => r);

            var existingShifts = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == shiftDate)
                .ToListAsync(cancellationToken);

            var existingShiftDict = existingShifts
                .ToDictionary(s => (s.RiderId, s.WorkingId), s => s);

            var oldComparisons = await dbcontext.TempRiderShiftComparisons
                .Where(t => t.ShiftDate == shiftDate && !t.IsResolved)
                .ToListAsync(cancellationToken);

            if (oldComparisons.Any())
            {
                dbcontext.TempRiderShiftComparisons.RemoveRange(oldComparisons);
                await dbcontext.SaveChangesAsync(cancellationToken);
            }

            var rows = worksheet.RowsUsed().Skip(1);
            var rowNumber = 1;
            var tempComparisons = new List<TempRiderShiftComparison>();

            foreach (var row in rows)
            {
                rowNumber++;

                try
                {
                    var shiftData = ParseExcelRowByName(row, columnMapping, rowNumber);
                    if (!shiftData.IsValid)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId?.ToString() ?? "N/A",
                            shiftData.ErrorMessage!));
                        continue;
                    }

                    RiderDetails? actualRider = null;
                    bool isSubstitution = false;
                    int? originalRiderWorkingId = null;

                    if (substitutionDict.TryGetValue(shiftData.WorkingId!.Value, out var substitution))
                    {
                        actualRider = substitution.ActualRider;
                        isSubstitution = true;
                        originalRiderWorkingId = actualRider.WorkingId; 

                        //Console.WriteLine($"[SUBSTITUTION] Rider {actualRider.Employee.NameEN} (Original ID: {originalRiderWorkingId}) " +
                        //                $"working under substitute ID {shiftData.WorkingId}");
                    }
                    else if (ridersByWorkingId.TryGetValue(shiftData.WorkingId!.Value, out var rider))
                    {
                        actualRider = rider;
                        isSubstitution = false;
                        originalRiderWorkingId = null;
                    }

                    if (actualRider is null)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"No rider found with working ID {shiftData.WorkingId}. " +
                            $"Check if rider exists or if there's an active substitution."));
                        continue;
                    }

                    var duplicateInBatch = tempComparisons.Any(t =>
                        t.RiderId == actualRider.Id &&
                        t.WorkingId == shiftData.WorkingId!.Value &&
                        t.ShiftDate == shiftDate);

                    if (duplicateInBatch)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"Duplicate entry in Excel for Working ID {shiftData.WorkingId} " +
                            $"(Rider: {actualRider.Employee.NameEN})"));
                        continue;
                    }

                    var newShiftStatus = CalculateShiftStatus(
                        shiftData.AcceptedDailyOrders!.Value,
                        actualRider.Company.Name);

                    var newRealRejectedOrders = Math.Max(0, shiftData.RejectedDailyOrders!.Value - rejectionThreshold);

                    var realRejectedOrders = Math.Max(0, shiftData.RejectedDailyOrders!.Value - rejectionThreshold);


                    var (hasNewRejectionProblem, newPenalty) = CalculateRejectionPenalty(newRealRejectedOrders);


                    var existingShift = existingShiftDict
                        .GetValueOrDefault((actualRider.Id, shiftData.WorkingId.Value));

                    var tempComparison = new TempRiderShiftComparison
                    {
                        RiderId = actualRider.Id,  
                        WorkingId = shiftData.WorkingId.Value, 
                        ShiftDate = shiftDate,
                        CompanyId = actualRider.CompanyId,

                        IsSubstitution = isSubstitution,
                        OriginalRiderWorkingId = originalRiderWorkingId,

                        OldAcceptedDailyOrders = existingShift?.AcceptedDailyOrders,
                        OldRejectedDailyOrders = existingShift?.RejectedDailyOrders,
                        OldRealRejectedDailyOrders = existingShift?.RealRejectedDailyOrders,
                        OldWorkingHours = existingShift?.WorkingHours,
                        OldShiftStatus = existingShift?.ShiftStatus,
                        OldCreatedAt = existingShift?.CreatedAt,
                        OldStackedDeliveries = existingShift?.StackedDeliveries,  // ADD THIS

                        NewAcceptedDailyOrders = shiftData.AcceptedDailyOrders.Value,
                        NewRejectedDailyOrders = shiftData.RejectedDailyOrders.Value,
                        NewWorkingHours = shiftData.WorkingHours.Value,

                        NewRealRejectedDailyOrders = newRealRejectedOrders,  // USE CALCULATED VALUE
                        NewStackedDeliveries = shiftData.StackedDeliveries.Value,
                        NewShiftStatus = newShiftStatus,

                        UploadedAt = DateTime.UtcNow,
                        IsResolved = false,

                        //Rider = actualRider,
                        //Company = actualRider.Company
                    };

                    tempComparisons.Add(tempComparison);

                    if (existingShift == null)
                        newShiftCount++;
                    else
                        updateCount++;

                    if (isSubstitution)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"INFO: Rider {actualRider.Employee.NameEN} (Original ID: {originalRiderWorkingId}) " +
                            $"is working under substitute ID {shiftData.WorkingId}"));
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

            if (tempComparisons.Any())
            {
                await dbcontext.TempRiderShiftComparisons.AddRangeAsync(
                    tempComparisons,
                    cancellationToken);
                await dbcontext.SaveChangesAsync(cancellationToken);

                foreach (var temp in tempComparisons)
                {
                    var (hasOldProblem, oldPenalty) = temp.OldRealRejectedDailyOrders.HasValue
                        ? CalculateRejectionPenalty(temp.OldRealRejectedDailyOrders.Value)
                        : (false, 0m);

                    var (hasNewProblem, newPenalty) =
                        CalculateRejectionPenalty(temp.NewRealRejectedDailyOrders);

                    var oldData = new ShiftComparisonData(
                        temp.OldAcceptedDailyOrders,
                        temp.OldRejectedDailyOrders,
                        temp.OldRealRejectedDailyOrders,
                        temp.OldStackedDeliveries,
                        temp.OldWorkingHours,
                        temp.OldShiftStatus,
                        hasOldProblem,
                        oldPenalty,
                        temp.OldCreatedAt
                    );

                    var newData = new ShiftComparisonData(
                        temp.NewAcceptedDailyOrders,
                        temp.NewRejectedDailyOrders,
                        temp.NewRealRejectedDailyOrders,
                        temp.NewStackedDeliveries,
                        temp.NewWorkingHours,
                        temp.NewShiftStatus,
                        hasNewProblem,
                        newPenalty,
                        temp.UploadedAt
                    );

                    var analysis = CreateComparisonAnalysis(oldData, newData);

                    var substitutionNote = temp.IsSubstitution
                        ? $"⚠️ Using substitute Working ID {temp.WorkingId} (Original ID: {temp.OriginalRiderWorkingId})"
                        : string.Empty;

                    comparisons.Add(new ShiftComparisonResponse(
                        temp.RiderId,
                        temp.WorkingId,
                        temp.ShiftDate,
                        temp.Rider.Employee.NameEN,
                        temp.Rider.Employee.NameAR,
                        temp.Company.Name,
                        CompanyShiftConfiguration.GetDailyOrderTarget(temp.Company.Name),
                        temp.IsSubstitution,  
                        temp.OriginalRiderWorkingId,  
                        substitutionNote,  
                        oldData,
                        newData,
                        analysis
                    ));
                }
            }

            var result = new BulkComparisonResult(
                tempComparisons.Count,
                newShiftCount,
                updateCount,
                comparisons,
                errors
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkComparisonResult>(
                new Error("ServerError", $"Error processing Excel: {ex.Message}", 500));
        }
    }

    public async Task<Result<BulkComparisonResult>> GetPendingComparisonsAsync(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tempComparisons = await dbcontext.TempRiderShiftComparisons
                .Include(t => t.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(t => t.Company)
                .Where(t => t.ShiftDate == shiftDate && !t.IsResolved)
                .OrderBy(t => t.WorkingId)
                .ToListAsync(cancellationToken);

            if (!tempComparisons.Any())
            {
                return Result.Failure<BulkComparisonResult>(
                    new Error("NotFound", "No pending comparisons found for this date", 404));
            }

            var comparisons = new List<ShiftComparisonResponse>();
            var newShiftCount = 0;
            var updateCount = 0;

            foreach (var temp in tempComparisons)
            {
                if (!temp.OldAcceptedDailyOrders.HasValue)
                    newShiftCount++;
                else
                    updateCount++;

                var (hasOldProblem, oldPenalty) = temp.OldRealRejectedDailyOrders.HasValue
                    ? CalculateRejectionPenalty(temp.OldRealRejectedDailyOrders.Value)
                    : (false, 0m);

                var (hasNewProblem, newPenalty) =
                    CalculateRejectionPenalty(temp.NewRealRejectedDailyOrders);

                var oldData = new ShiftComparisonData(
                        temp.OldAcceptedDailyOrders,
                        temp.OldRejectedDailyOrders,
                        temp.OldRealRejectedDailyOrders,
                        temp.OldStackedDeliveries,
                        temp.OldWorkingHours,
                        temp.OldShiftStatus,
                        hasOldProblem,
                        oldPenalty,
                        temp.OldCreatedAt
                    );

                var newData = new ShiftComparisonData(
                    temp.NewAcceptedDailyOrders,
                    temp.NewRejectedDailyOrders,
                    temp.NewRealRejectedDailyOrders,
                    temp.NewStackedDeliveries,
                    temp.NewWorkingHours,
                    temp.NewShiftStatus,
                    hasNewProblem,
                    newPenalty,
                    temp.UploadedAt
                );

                var analysis = CreateComparisonAnalysis(oldData, newData);

                var substitutionNote = temp.IsSubstitution
                    ? $"⚠️ Using substitute Working ID {temp.WorkingId} (Original ID: {temp.OriginalRiderWorkingId})"
                    : string.Empty;

                comparisons.Add(new ShiftComparisonResponse(
                    temp.RiderId,
                    temp.WorkingId,
                    temp.ShiftDate,
                    temp.Rider.Employee.NameEN,
                    temp.Rider.Employee.NameAR,
                    temp.Company.Name,
                    CompanyShiftConfiguration.GetDailyOrderTarget(temp.Company.Name),
                    temp.IsSubstitution,  
                    temp.OriginalRiderWorkingId,  
                    substitutionNote,  
                    oldData,
                    newData,
                    analysis
                ));
            }

            var result = new BulkComparisonResult(
                tempComparisons.Count,
                newShiftCount,
                updateCount,
                comparisons,
                new List<ImportError>()
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkComparisonResult>(
                new Error("ServerError", $"Error retrieving comparisons: {ex.Message}", 500));
        }
    }


    public async Task<Result<ResolutionResult>> ResolveShiftComparisonsAsync(
        ResolveComparisonsRequest request,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var tempComparisons = await dbcontext.TempRiderShiftComparisons
                .Include(t => t.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(t => t.Company)
                .Where(t => t.ShiftDate == request.ShiftDate && !t.IsResolved)
                .ToListAsync(cancellationToken);

            if (!tempComparisons.Any())
            {
                return Result.Failure<ResolutionResult>(
                    new Error("NotFound", "No pending comparisons to resolve", 404));
            }

            var details = new List<string>();
            var updatedCount = 0;
            var newCount = 0;
            var unchangedCount = 0;

            if (request.Choice == ResolutionChoice.KeepOld)
            {
                foreach (var temp in tempComparisons)
                {
                    temp.IsResolved = true;

                    var riderInfo = temp.IsSubstitution
                        ? $"{temp.Rider.Employee.NameEN} (Substitute ID: {temp.WorkingId}, Original ID: {temp.OriginalRiderWorkingId})"
                        : $"{temp.Rider.Employee.NameEN} (ID: {temp.WorkingId})";

                    details.Add($"Kept existing data for {riderInfo}");
                }
                unchangedCount = tempComparisons.Count;
            }
            else if (request.Choice == ResolutionChoice.UseNew)
            {
                var shiftKeys = tempComparisons
                    .Select(t => new { t.RiderId, t.WorkingId, t.ShiftDate })
                    .ToList();

                // Get RiderId, WorkingId, ShiftDate from DB instead of using an in-memory list inside the query
                var riderIds = shiftKeys.Select(x => x.RiderId).ToList();
                var workingIds = shiftKeys.Select(x => x.WorkingId).ToList();
                var dates = shiftKeys.Select(x => x.ShiftDate).ToList();

                var existingShifts = await dbcontext.RiderShifts
                    .Where(s =>
                        riderIds.Contains(s.RiderId) &&
                        workingIds.Contains(s.WorkingId) &&
                        dates.Contains(s.ShiftDate))
                    .ToListAsync(cancellationToken);


                var existingShiftDict = existingShifts
                    .ToDictionary(s => (s.RiderId, s.WorkingId, s.ShiftDate), s => s);

                foreach (var temp in tempComparisons)
                {
                    var shiftKey = (temp.RiderId, temp.WorkingId, temp.ShiftDate);

                    if (existingShiftDict.TryGetValue(shiftKey, out var existingShift))
                    {
                        existingShift.AcceptedDailyOrders = temp.NewAcceptedDailyOrders;
                        existingShift.RejectedDailyOrders = temp.NewRejectedDailyOrders;
                        existingShift.RealRejectedDailyOrders = temp.NewRealRejectedDailyOrders;
                        existingShift.WorkingHours = temp.NewWorkingHours;
                        existingShift.ShiftStatus = temp.NewShiftStatus;

                        updatedCount++;

                        var riderInfo = temp.IsSubstitution
                            ? $"{temp.Rider.Employee.NameEN} (Substitute ID: {temp.WorkingId}, Original ID: {temp.OriginalRiderWorkingId})"
                            : $"{temp.Rider.Employee.NameEN} (ID: {temp.WorkingId})";

                        details.Add($"Updated shift for {riderInfo}");
                    }
                    else
                    {
                        var newShift = new RiderShift
                        {
                            RiderId = temp.RiderId,  
                            WorkingId = temp.WorkingId,  
                            ShiftDate = temp.ShiftDate,
                            AcceptedDailyOrders = temp.NewAcceptedDailyOrders,
                            RejectedDailyOrders = temp.NewRejectedDailyOrders,
                            RealRejectedDailyOrders = temp.NewRealRejectedDailyOrders,
                            WorkingHours = temp.NewWorkingHours,
                            CompanyId = temp.CompanyId,
                            ShiftStatus = temp.NewShiftStatus,
                            CreatedAt = DateTime.UtcNow
                        };

                        await dbcontext.RiderShifts.AddAsync(newShift, cancellationToken);
                        newCount++;

                        var riderInfo = temp.IsSubstitution
                            ? $"{temp.Rider.Employee.NameEN} (Substitute ID: {temp.WorkingId}, Original ID: {temp.OriginalRiderWorkingId})"
                            : $"{temp.Rider.Employee.NameEN} (ID: {temp.WorkingId})";

                        details.Add($"Created new shift for {riderInfo}");
                    }

                    temp.IsResolved = true;
                }
            }

            await dbcontext.SaveChangesAsync(cancellationToken);

            dbcontext.TempRiderShiftComparisons.RemoveRange(tempComparisons);
            await dbcontext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var result = new ResolutionResult(
                tempComparisons.Count,
                updatedCount,
                newCount,
                unchangedCount,
                details
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<ResolutionResult>(
                new Error("ServerError", $"Error resolving comparisons: {ex.Message}", 500));
        }
    }

    private static ComparisonAnalysis CreateComparisonAnalysis(
        ShiftComparisonData oldData,
        ShiftComparisonData newData)
    {
        var hasChanges = oldData.AcceptedOrders.HasValue &&
            (oldData.AcceptedOrders != newData.AcceptedOrders ||
             oldData.RejectedOrders != newData.RejectedOrders ||
             oldData.RealRejectedOrders != newData.RealRejectedOrders ||
             oldData.StackedDeliveries != newData.StackedDeliveries ||  // ADD THIS
             Math.Abs(oldData.WorkingHours!.Value - newData.WorkingHours!.Value) > 0.01f);

        var ordersDiff = newData.AcceptedOrders!.Value - (oldData.AcceptedOrders ?? 0);
        var rejectionsDiff = newData.RealRejectedOrders!.Value - (oldData.RealRejectedOrders ?? 0);
        var hoursDiff = newData.WorkingHours!.Value - (oldData.WorkingHours ?? 0);
        var penaltyDiff = newData.PenaltyAmount!.Value - (oldData.PenaltyAmount ?? 0);
        var stackedDeliveriesDiff = newData.StackedDeliveries!.Value - (oldData.StackedDeliveries ?? 0);

        var statusChange = oldData.ShiftStatus != null
            ? $"{oldData.ShiftStatus} → {newData.ShiftStatus}"
            : $"New: {newData.ShiftStatus}";

        string recommendation;
        if (!oldData.AcceptedOrders.HasValue)
        {
            recommendation = "New shift - accept to add to database";
        }
        else if (!hasChanges)
        {
            recommendation = "No changes detected";
        }
        else if (ordersDiff > 0 && rejectionsDiff <= 0)
        {
            recommendation = "Improvement - consider accepting new data";
        }
        else if (ordersDiff < 0 || rejectionsDiff > 0)
        {
            recommendation = "Performance decline - verify data accuracy";
        }
        else
        {
            recommendation = "Mixed changes - review carefully";
        }

        return new ComparisonAnalysis(
            hasChanges,
            ordersDiff,
            rejectionsDiff,
            hoursDiff,
            statusChange,
            penaltyDiff,
            recommendation,
            stackedDeliveriesDiff
        );
    }



    private static string CalculateShiftStatus(int acceptedOrders, string companyName)
    {
        var thresholds = new Dictionary<string, (int Poor, int Average, int Good, int Excellent)>
    {
        { "Jahez", (15, 25, 35, 45) },
        { "HungerStation", (12, 20, 30, 40) },
        { "Careem", (10, 18, 28, 38) },
        { "Marsool", (8, 15, 25, 35) }
    };

        var (poor, average, good, excellent) = thresholds.ContainsKey(companyName)
            ? thresholds[companyName]
            : (10, 20, 30, 40);

        if (acceptedOrders >= excellent)
            return "Excellent";
        else if (acceptedOrders >= good)
            return "Good";
        else if (acceptedOrders >= average)
            return "Average";
        else if (acceptedOrders >= poor)
            return "Poor";
        else
            return "VeryPoor";
    }

    private static (bool hasRejectionProblem, decimal penaltyAmount) CalculateRejectionPenalty(int realRejections)
    {
        const decimal penaltyPerRejection = 10m;
        const int freeRejections = 2;

        if (realRejections <= freeRejections)
            return (false, 0m);

        var penalty = (realRejections - freeRejections) * penaltyPerRejection;

        return (true, penalty);
    }


    private static (
        bool IsValid,
        int? WorkingId,
        int? AcceptedDailyOrders,
        int? RejectedDailyOrders,
        int? StackedDeliveries,         // ADD THIS TO SIGNATURE
        float? WorkingHours,
        string? ErrorMessage) ParseExcelRowByName(IXLRow row, ExcelColumnMapping mapping, int rowNumber)
    {
        try
        {
            var workingIdCell = row.Cell(mapping.WorkingIdColumn).Value;
            if (!int.TryParse(workingIdCell.ToString(), out var workingId))
                return (false, null, null, null, null, null, "Invalid Working ID");

            var acceptedCell = row.Cell(mapping.AcceptedOrdersColumn).Value;
            if (!int.TryParse(acceptedCell.ToString(), out var acceptedOrders) || acceptedOrders < 0)
                return (false, workingId, null, null, null, null, "Invalid Accepted Orders (must be >= 0)");

            var rejectedCell = row.Cell(mapping.RejectedOrdersColumn).Value;
            if (!int.TryParse(rejectedCell.ToString(), out var rejectedOrders) || rejectedOrders < 0)
                return (false, workingId, acceptedOrders, null, null, null, "Invalid Rejected Orders (must be >= 0)");

            var stackedCell = row.Cell(mapping.StackedDeliveriesColumn).Value;
            if (!int.TryParse(stackedCell.ToString(), out var stackedDeliveries) || stackedDeliveries < 0)
                return (false, workingId, acceptedOrders, rejectedOrders, null, null, "Invalid Stacked Deliveries");

            var hoursCell = row.Cell(mapping.WorkingHoursColumn).Value;
            if (!float.TryParse(hoursCell.ToString(), out var workingHours) || workingHours < 0 || workingHours > 24)
                return (false, workingId, acceptedOrders, rejectedOrders, stackedDeliveries, null, "Invalid Working Hours");



            //var hoursCell = row.Cell(mapping.WorkingHoursColumn).Value;
            //if (!float.TryParse(hoursCell.ToString(), out var workingHours) || workingHours < 0 || workingHours > 24)
            //    return (false, workingId, acceptedOrders, rejectedOrders, realRejectedOrders, null, "Invalid Working Hours (must be 0-24)");

            return (true, workingId, acceptedOrders, rejectedOrders, stackedDeliveries, workingHours, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, null, null, null, $"Error: {ex.Message}");
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

        mapping.WorkingIdColumn = FindColumn(headerCells, ExcelColumnConfig.WorkingIdColumns);
        mapping.AcceptedOrdersColumn = FindColumn(headerCells, ExcelColumnConfig.AcceptedOrdersColumns);
        mapping.RejectedOrdersColumn = FindColumn(headerCells, ExcelColumnConfig.RejectedOrdersColumns);
        mapping.StackedDeliveriesColumn = FindColumn(headerCells, ExcelColumnConfig.StackedDeliveriesColumns);
        mapping.WorkingHoursColumn = FindColumn(headerCells, ExcelColumnConfig.WorkingHoursColumns);

        var missingColumns = new List<string>();

        if (mapping.WorkingIdColumn == 0)
            missingColumns.Add($"WorkingId (tried: {string.Join(", ", ExcelColumnConfig.WorkingIdColumns)})");
        if (mapping.AcceptedOrdersColumn == 0)
            missingColumns.Add($"AcceptedOrders (tried: {string.Join(", ", ExcelColumnConfig.AcceptedOrdersColumns)})");
        if (mapping.RejectedOrdersColumn == 0)
            missingColumns.Add($"RejectedOrders (tried: {string.Join(", ", ExcelColumnConfig.RejectedOrdersColumns)})");
        if (mapping.StackedDeliveriesColumn == 0)
            missingColumns.Add($"RealRejectedOrders (tried: {string.Join(", ", ExcelColumnConfig.StackedDeliveriesColumns)})");
        if (mapping.WorkingHoursColumn == 0)
            missingColumns.Add($"WorkingHours (tried: {string.Join(", ", ExcelColumnConfig.WorkingHoursColumns)})");

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


    public async Task<Result<RiderShiftResponse>> CreateShiftAsync(
       CreateRiderShiftRequest request,
       CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            const int rejectionThreshold = 2; // Or from config
            var realRejectedOrders = Math.Max(0, request.RejectedDailyOrders - rejectionThreshold);

            var (actualRiderId, originalWorkingId, isSubstitution) =
                await GetActualRiderAsync(request.WorkingId, cancellationToken);

            if (actualRiderId == 0)
                return Result.Failure<RiderShiftResponse>(
                    new Error("NotFound", $"No rider found with working ID {request.WorkingId}", 404));

            var existingShift = await dbcontext.RiderShifts
                .AnyAsync(s => s.RiderId == actualRiderId &&
                              s.WorkingId == request.WorkingId &&
                              s.ShiftDate == request.ShiftDate,
                         cancellationToken);

            if (existingShift)
                return Result.Failure<RiderShiftResponse>(
                    new Error("AlreadyExists", "Shift already exists for this date and working ID", 400));

            var riderDetails = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.Id == actualRiderId, cancellationToken);

            if (riderDetails is null)
                return Result.Failure<RiderShiftResponse>(
                    new Error("NotFound", "Rider details not found", 404));

            var shiftStatus = CalculateShiftStatus(
                request.AcceptedDailyOrders,
                riderDetails.Company.Name);

            var (hasRejectionProblem, penaltyAmount) = CalculateRejectionPenalty(request.RealRejectedDailyOrders);

            var shift = new RiderShift
            {
                RiderId = actualRiderId,
                WorkingId = request.WorkingId,
                ShiftDate = request.ShiftDate,
                AcceptedDailyOrders = request.AcceptedDailyOrders,
                RejectedDailyOrders = request.RejectedDailyOrders,
                WorkingHours = request.WorkingHours,
                CompanyId = riderDetails.CompanyId,
                ShiftStatus = shiftStatus.ToString(),
                RealRejectedDailyOrders = realRejectedOrders,  
                StackedDeliveries = request.StackedDeliveries,
                CreatedAt = DateTime.UtcNow
            };

            await dbcontext.RiderShifts.AddAsync(shift, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = new RiderShiftResponse(
                shift.RiderId,
                shift.WorkingId,
                shift.ShiftDate,
                shift.AcceptedDailyOrders,
                shift.RejectedDailyOrders,
                shift.RealRejectedDailyOrders,
                shift.StackedDeliveries,
                shift.WorkingHours,
                shift.CompanyId,
                riderDetails.Company.Name,
                riderDetails.Employee.NameEN,
                shiftStatus,
                hasRejectionProblem,
                penaltyAmount,
                shift.CreatedAt,
                isSubstitution,
                originalWorkingId
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RiderShiftResponse>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<RiderShiftResponse>> GetShiftAsync(
        int workingId,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var shift = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .FirstOrDefaultAsync(s =>
                                         s.WorkingId == workingId &&
                                         s.ShiftDate == shiftDate,
                                    cancellationToken);

            if (shift is null)
                return Result.Failure<RiderShiftResponse>(
                    new Error("NotFound", "Shift not found", 404));

            var response = MapToResponse(shift);
            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderShiftResponse>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderShiftResponse>>> GetShiftsByRiderAsync(
        int WorkingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var shifts = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .Where(s => s.WorkingId == WorkingId)
                .OrderByDescending(s => s.ShiftDate)
                .ToListAsync(cancellationToken);

            var responses = shifts.Select(MapToResponse);
            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderShiftResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderShiftResponse>>> GetShiftsByDateAsync(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var shifts = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .Where(s => s.ShiftDate == shiftDate)
                .OrderBy(s => s.WorkingId)
                .ToListAsync(cancellationToken);

            var responses = shifts.Select(MapToResponse);
            return Result.Success<IEnumerable<RiderShiftResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderShiftResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<IEnumerable<RiderShiftResponse>>> GetShiftsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var shifts = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                .OrderBy(s => s.ShiftDate)
                .ThenBy(s => s.WorkingId)
                .ToListAsync(cancellationToken);

            var responses = shifts.Select(MapToResponse);
            return Result.Success<IEnumerable<RiderShiftResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderShiftResponse>>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<RiderShiftResponse>> UpdateShiftAsync(
        UpdateRiderShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var (riderId, _, _) = await GetActualRiderAsync(request.WorkingId, cancellationToken);

            var shift = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .FirstOrDefaultAsync(s => s.RiderId == riderId &&
                                         s.WorkingId == request.WorkingId &&
                                         s.ShiftDate == request.ShiftDate,
                                    cancellationToken);

            if (shift is null)
                return Result.Failure<RiderShiftResponse>(
                    new Error("NotFound", "Shift not found", 404));

            if (request.AcceptedDailyOrders.HasValue)
                shift.AcceptedDailyOrders = request.AcceptedDailyOrders.Value;

            if (request.RejectedDailyOrders.HasValue)
                shift.RejectedDailyOrders = request.RejectedDailyOrders.Value;

            if (request.RejectedDailyOrders.HasValue)
            {
                const int rejectionThreshold = 2;
                shift.RealRejectedDailyOrders = Math.Max(0, shift.RejectedDailyOrders - rejectionThreshold);
            }

            if (request.WorkingHours.HasValue)
                shift.WorkingHours = request.WorkingHours.Value;
            if (request.StackedDeliveries.HasValue)
                shift.StackedDeliveries = request.StackedDeliveries.Value;


            var newStatus = CalculateShiftStatus(
                shift.AcceptedDailyOrders,
                shift.Rider.Company.Name);

            shift.ShiftStatus = newStatus.ToString();

            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = MapToResponse(shift);
            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RiderShiftResponse>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result> DeleteShiftAsync(
        int workingId,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var shift = await dbcontext.RiderShifts
                .FirstOrDefaultAsync(s =>
                                         s.WorkingId == workingId &&
                                         s.ShiftDate == shiftDate,
                                    cancellationToken);

            if (shift is null)
                return Result.Failure(new Error("NotFound", "Shift not found", 404));

            dbcontext.RiderShifts.Remove(shift);
            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("ServerError", ex.Message, 500));
        }
    }

    private async Task<(int riderId, int? originalWorkingId, bool isSubstitution)> GetActualRiderAsync(
        int workingId,
        CancellationToken cancellationToken)
    {
        var substitution = await dbcontext.Set<RiderShiftSubstitution>()
            .Include(s => s.ActualRider)
            .FirstOrDefaultAsync(s => s.SubstituteWorkingId == workingId && s.IsActive,
                                cancellationToken);

        if (substitution != null)
        {
            return (substitution.ActualRiderId, workingId, true);
        }

        var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

        return rider != null ? (rider.Id, null, false) : (0, null, false);
    }

    private static RiderShiftResponse MapToResponse(RiderShift shift)
    {
        // Parse shift status safely
        var shiftStatus = Enum.TryParse<ShiftStatus>(shift.ShiftStatus, out var status)
            ? status
            : ShiftStatus.Average; // Default if parsing fails

        // Calculate rejection penalty
        var (hasRejectionProblem, penaltyAmount) = CalculateRejectionPenalty(shift.RealRejectedDailyOrders);

        return new RiderShiftResponse(
            shift.RiderId,
            shift.WorkingId,
            shift.ShiftDate,
            shift.AcceptedDailyOrders,
            shift.RejectedDailyOrders,
            shift.RealRejectedDailyOrders,
            shift.StackedDeliveries,  // ADD THIS in appropriate position
            shift.WorkingHours,
            shift.CompanyId,
            shift.Rider?.Company?.Name ?? "Unknown", // Null-safe access
            shift.Rider?.Employee?.NameEN ?? "Unknown", // Null-safe access
            shiftStatus.ToString(),
            hasRejectionProblem,
            penaltyAmount,
            shift.CreatedAt,
            false,
            null
        );
    }

    public async Task<Result<List<RiderShiftResponse>>> GetRiderShiftsByDateOptimizedAsync(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var shifts = await dbcontext.RiderShifts
                .AsNoTracking() 
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Company)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == shiftDate)
                .OrderBy(s => s.WorkingId)
                .ToListAsync(cancellationToken);

            var responses = shifts.Select(MapToResponse).ToList();

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<RiderShiftResponse>>(
                new Error("ServerError", $"Error retrieving shifts: {ex.Message}", 500));
        }
    }


    public async Task<Result<BulkDeleteResult>> DeleteShiftsByDateAsync(
       DateOnly shiftDate,
       CancellationToken cancellationToken = default)
    {
        try
        {
            var shiftsToDelete = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate == shiftDate)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Company)
                .ToListAsync(cancellationToken);

            if (!shiftsToDelete.Any())
            {
                return Result.Success(new BulkDeleteResult(0, new List<string>()));
            }

            var deletedDetails = shiftsToDelete
                .Select(s => $"Rider: {s.Rider.Employee.NameEN} (ID: {s.WorkingId}), Date: {s.ShiftDate}, Company: {s.Company.Name}")
                .ToList();

            dbcontext.RiderShifts.RemoveRange(shiftsToDelete);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new BulkDeleteResult(
                shiftsToDelete.Count,
                deletedDetails
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkDeleteResult>(
                new Error("ServerError", $"Failed to delete shifts: {ex.Message}", 500));
        }
    }

    public async Task<Result<BulkDeleteResult>> DeleteShiftsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<BulkDeleteResult>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            var shiftsToDelete = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Company)
                .ToListAsync(cancellationToken);

            if (!shiftsToDelete.Any())
            {
                return Result.Success(new BulkDeleteResult(0, new List<string>()));
            }

            var deletedDetails = shiftsToDelete
                .Select(s => $"Rider: {s.Rider.Employee.NameEN} (ID: {s.WorkingId}), Date: {s.ShiftDate}, Company: {s.Company.Name}")
                .ToList();

            dbcontext.RiderShifts.RemoveRange(shiftsToDelete);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new BulkDeleteResult(
                shiftsToDelete.Count,
                deletedDetails
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkDeleteResult>(
                new Error("ServerError", $"Failed to delete shifts: {ex.Message}", 500));
        }
    }

    public async Task<Result<BulkDeleteResult>> DeleteShiftsByRiderAndDateRangeAsync(
        int workingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (startDate > endDate)
            {
                return Result.Failure<BulkDeleteResult>(
                    new Error("InvalidInput", "Start date must be before or equal to end date", 400));
            }

            // Verify rider exists
            var riderExists = await dbcontext.RiderDetails
                .AnyAsync(r => r.WorkingId == workingId, cancellationToken);

            if (!riderExists)
            {
                return Result.Failure<BulkDeleteResult>(
                    new Error("NotFound", "Rider not found", 404));
            }

            var shiftsToDelete = await dbcontext.RiderShifts
                .Where(s => s.RiderId == workingId &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Company)
                .ToListAsync(cancellationToken);

            if (!shiftsToDelete.Any())
            {
                return Result.Success(new BulkDeleteResult(0, new List<string>()));
            }

            var deletedDetails = shiftsToDelete
                .Select(s => $"Date: {s.ShiftDate}, Company: {s.Company.Name}, Working ID: {s.WorkingId}, Orders: {s.AcceptedDailyOrders}")
                .ToList();

            dbcontext.RiderShifts.RemoveRange(shiftsToDelete);
            await dbcontext.SaveChangesAsync(cancellationToken);

            return Result.Success(new BulkDeleteResult(
                shiftsToDelete.Count,
                deletedDetails
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkDeleteResult>(
                new Error("ServerError", $"Failed to delete shifts: {ex.Message}", 500));
        }
    }

}


public static class ExcelColumnConfig
{
    public static readonly string[] WorkingIdColumns =
        { "Rider Id", "Working_ID", "Working ID", "ID", "RiderID", "Rider_ID", "EmployeeID" };

    //public static readonly string[] ShiftDateColumns =
    //    { "ShiftDate", "Shift_Date", "Shift Date", "Date", "WorkDate", "Work_Date" };

    public static readonly string[] AcceptedOrdersColumns =
        { "Completed Deliveries", "Accepted_Orders", "Accepted Orders", "Accepted", "AcceptedDaily", "Accepted_Daily" };

    public static readonly string[] RejectedOrdersColumns =
        { "Declined Deliveries", "Rejected_Orders", "Rejected Orders", "Rejected", "RejectedDaily", "Rejected_Daily" };

    //public static readonly string[] RealRejectedOrdersColumns =
    //    { "RealRejectedOrders", "Real_Rejected_Orders", "Real Rejected Orders", "Real Rejected", "ActualRejected", "Actual_Rejected" };

    public static readonly string[] StackedDeliveriesColumns =
        { "Stacked Deliveries", "Stacked_Deliveries", "StackedDeliveries"};


    public static readonly string[] WorkingHoursColumns =
        { "Actual Working Hours", "Working_Hours", "Working Hours", "Hours", "TotalHours", "Total_Hours" };
}

public class ShiftConflictDto
{
    public int RiderId { get; set; }
    public int WorkingId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public int RowNumber { get; set; }
    public string RiderName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public ShiftDto ExistingShift { get; set; } = null!;
    public ShiftDto NewShift { get; set; } = null!;
}

public class ShiftDto
{
    public int AcceptedOrders { get; set; }
    public int RejectedOrders { get; set; }
    public int RealRejectedOrders { get; set; }
    public int StackedDeliveries { get; set; }  // ADD THIS
    public float WorkingHours { get; set; }
    public string ShiftStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ConflictResolutionChoice
{
    public int RiderId { get; set; }
    public int WorkingId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public ConflictResolution Resolution { get; set; }
}

public enum ConflictResolution
{
    KeepNewest,
    KeepOldest
}

public class BulkImportResult
{
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<ImportError> Errors { get; set; }
    public int ConflictCount { get; set; }
    public List<ShiftConflictDto> Conflicts { get; set; }

    public BulkImportResult(int totalRecords, int successCount, int errorCount,
        List<ImportError> errors, int conflictCount = 0, List<ShiftConflictDto>? conflicts = null)
    {
        TotalRecords = totalRecords;
        SuccessCount = successCount;
        ErrorCount = errorCount;
        Errors = errors;
        ConflictCount = conflictCount;
        Conflicts = conflicts ?? new List<ShiftConflictDto>();
    }
}

public class ExcelColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int WorkingIdColumn { get; set; }
    public int AcceptedOrdersColumn { get; set; }
    public int RejectedOrdersColumn { get; set; }
    public int StackedDeliveriesColumn { get; set; }
    public int WorkingHoursColumn { get; set; }
}

public class ImportError
{

    public int RowNumber { get; set; }
    public string WorkingId { get; set; }
    public string Message { get; set; }

    public ImportError(int rowNumber, string workingId, string message)
    {
        RowNumber = rowNumber;
        WorkingId = workingId;
        Message = message;
    }
}

public record BulkComparisonResult(
    int TotalComparisons,
    int NewShifts,
    int UpdatedShifts,
    List<ShiftComparisonResponse> Comparisons,
    List<ImportError> Errors
);