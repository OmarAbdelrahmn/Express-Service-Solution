using Application.Abstraction;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;


namespace Application.Service.Riders;

public class RiderShiftService(ApplicationDbcontext dbcontext) : IRiderShiftService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    //public async Task<Result<BulkImportResult>> ImportShiftsFromExcelAsync(
    //    Stream excelStream,
    //    CancellationToken cancellationToken = default)
    //{
    //    var errors = new List<ImportError>();
    //    var successCount = 0;
    //    var totalRecords = 0;

    //    try
    //    {
    //        using var workbook = new XLWorkbook(excelStream);
    //        var worksheet = workbook.Worksheet(1);
    //        var rows = worksheet.RowsUsed().Skip(1); // Skip header row

    //        totalRecords = rows.Count();

    //        // Pre-load all active substitutions for performance
    //        var activeSubstitutions = await dbcontext.Set<RiderShiftSubstitution>()
    //            .Where(s => s.IsActive)
    //            .Include(s => s.ActualRider)
    //                .ThenInclude(r => r.Company)
    //            .Include(s => s.ActualRider)
    //                .ThenInclude(r => r.Employee)
    //            .AsNoTracking()
    //            .ToListAsync(cancellationToken);

    //        var substitutionDict = activeSubstitutions
    //            .ToDictionary(s => s.SubstituteWorkingId, s => s);

    //        // Pre-load all rider details for performance
    //        var allRiderDetails = await dbcontext.RiderDetails
    //            .Include(r => r.Company)
    //            .Include(r => r.Employee)
    //            .AsNoTracking()
    //            .ToListAsync(cancellationToken);

    //        var ridersByWorkingId = allRiderDetails
    //            .Where(r => r.WorkingId.HasValue)
    //            .ToDictionary(r => r.WorkingId!.Value, r => r);

    //        // Prepare batch insert
    //        var shiftsToAdd = new List<RiderShift>();
    //        var rowNumber = 1;

    //        foreach (var row in rows)
    //        {
    //            rowNumber++;

    //            try
    //            {
    //                var shiftData = ParseExcelRow(row, rowNumber);

    //                if (!shiftData.IsValid)
    //                {
    //                    errors.Add(new ImportError(rowNumber, shiftData.WorkingId?.ToString() ?? "N/A", shiftData.ErrorMessage!));
    //                    continue;
    //                }

    //                // Determine actual rider (considering substitution)
    //                RiderDetails? actualRider = null;
    //                bool isSubstitution = false;

    //                if (substitutionDict.TryGetValue(shiftData.WorkingId!.Value, out var substitution))
    //                {
    //                    actualRider = substitution.ActualRider;
    //                    isSubstitution = true;
    //                }
    //                else if (ridersByWorkingId.TryGetValue(shiftData.WorkingId!.Value, out var rider))
    //                {
    //                    actualRider = rider;
    //                }

    //                if (actualRider is null)
    //                {
    //                    errors.Add(new ImportError(
    //                        rowNumber,
    //                        shiftData.WorkingId!.Value.ToString(),
    //                        $"No rider found with working ID {shiftData.WorkingId}"));
    //                    continue;
    //                }

    //                // Check for duplicate in current batch
    //                var duplicateInBatch = shiftsToAdd.Any(s =>
    //                    s.RiderId == actualRider.Id &&
    //                    s.WorkingId == shiftData.WorkingId &&
    //                    s.ShiftDate == shiftData.ShiftDate);

    //                if (duplicateInBatch)
    //                {
    //                    errors.Add(new ImportError(
    //                        rowNumber,
    //                        shiftData.WorkingId!.Value.ToString(),
    //                        $"Duplicate shift in Excel file for date {shiftData.ShiftDate}"));
    //                    continue;
    //                }

    //                // Calculate shift status based on company rules
    //                var shiftStatus = CalculateShiftStatus(
    //                    shiftData.AcceptedDailyOrders!.Value,
    //                    actualRider.Company.Name);

    //                // Check for rejection problems
    //                var (hasRejectionProblem, penaltyAmount) = CalculateRejectionPenalty(
    //                    shiftData.RealRejectedDailyOrders!.Value);

    //                var shift = new RiderShift
    //                {
    //                    RiderId = actualRider.Id,
    //                    WorkingId = shiftData.WorkingId!.Value,
    //                    ShiftDate = shiftData.ShiftDate!.Value,
    //                    AcceptedDailyOrders = shiftData.AcceptedDailyOrders!.Value,
    //                    RejectedDailyOrders = shiftData.RejectedDailyOrders!.Value,
    //                    RealRejectedDailyOrders = shiftData.RealRejectedDailyOrders!.Value,
    //                    WorkingHours = shiftData.WorkingHours!.Value,
    //                    CompanyId = actualRider.CompanyId,
    //                    ShiftStatus = shiftStatus.ToString(),
    //                    CreatedAt = DateTime.UtcNow
    //                };

    //                shiftsToAdd.Add(shift);

    //                // Log warning for problematic shifts
    //                if (hasRejectionProblem)
    //                {
    //                    errors.Add(new ImportError(
    //                        rowNumber,
    //                        shiftData.WorkingId!.Value.ToString(),
    //                        $"WARNING: Shift has {shiftData.RealRejectedDailyOrders} rejections (Penalty: {penaltyAmount} SAR)"));
    //                }
    //            }
    //            catch (Exception ex)
    //            {
    //                errors.Add(new ImportError(
    //                    rowNumber,
    //                    "N/A",
    //                    $"Error parsing row: {ex.Message}"));
    //            }
    //        }

    //        // Batch insert with duplicate check
    //        if (shiftsToAdd.Any())
    //        {
    //            using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

    //            try
    //            {
    //                // Check for existing shifts in database
    //                var shiftKeys = shiftsToAdd
    //                    .Select(s => new { s.RiderId, s.WorkingId, s.ShiftDate })
    //                    .ToList();

    //                var existingShifts = await dbcontext.RiderShifts
    //                    .Where(s => shiftKeys.Any(k =>
    //                        k.RiderId == s.RiderId &&
    //                        k.WorkingId == s.WorkingId &&
    //                        k.ShiftDate == s.ShiftDate))
    //                    .Select(s => new { s.RiderId, s.WorkingId, s.ShiftDate })
    //                    .ToListAsync(cancellationToken);

    //                var existingSet = existingShifts.ToHashSet();

    //                var newShifts = shiftsToAdd
    //                    .Where(s => !existingSet.Contains(new { s.RiderId, s.WorkingId, s.ShiftDate }))
    //                    .ToList();

    //                var duplicateCount = shiftsToAdd.Count - newShifts.Count;
    //                if (duplicateCount > 0)
    //                {
    //                    errors.Add(new ImportError(
    //                        0,
    //                        "Multiple",
    //                        $"{duplicateCount} shifts already exist in database and were skipped"));
    //                }

    //                if (newShifts.Any())
    //                {
    //                    await dbcontext.RiderShifts.AddRangeAsync(newShifts, cancellationToken);
    //                    await dbcontext.SaveChangesAsync(cancellationToken);
    //                    successCount = newShifts.Count;
    //                }

    //                await transaction.CommitAsync(cancellationToken);
    //            }
    //            catch (Exception ex)
    //            {
    //                await transaction.RollbackAsync(cancellationToken);
    //                return Result.Failure<BulkImportResult>(
    //                    new Error("ServerError", $"Database error during bulk insert: {ex.Message}", 500));
    //            }
    //        }

    //        var result = new BulkImportResult(
    //            totalRecords,
    //            successCount,
    //            errors.Count,
    //            errors
    //        );

    //        return Result.Success(result);
    //    }
    //    catch (Exception ex)
    //    {
    //        return Result.Failure<BulkImportResult>(
    //            new Error("ServerError", $"Error reading Excel file: {ex.Message}", 500));
    //    }
    //}

    //private static (
    //    bool IsValid,
    //    int? WorkingId,
    //    DateOnly? ShiftDate,
    //    int? AcceptedDailyOrders,
    //    int? RejectedDailyOrders,
    //    int? RealRejectedDailyOrders,
    //    float? WorkingHours,
    //    string? ErrorMessage) ParseExcelRow(IXLRow row, int rowNumber)
    //{
    //    try
    //    {
    //        // Expected columns: WorkingId, ShiftDate, AcceptedOrders, RejectedOrders, RealRejectedOrders, WorkingHours
    //        // Note: ShiftStatus is auto-calculated, not in Excel

    //        var workingIdCell = row.Cell(1).Value;
    //        if (!int.TryParse(workingIdCell.ToString(), out var workingId))
    //            return (false, null, null, null, null, null, null, "Invalid Working ID");

    //        var shiftDateCell = row.Cell(2).Value;
    //        DateOnly shiftDate;

    //        if (shiftDateCell.IsDateTime)
    //        {
    //            shiftDate = DateOnly.FromDateTime(shiftDateCell.GetDateTime());
    //        }
    //        else if (!DateOnly.TryParse(shiftDateCell.ToString(), out shiftDate))
    //        {
    //            return (false, workingId, null, null, null, null, null, "Invalid Shift Date");
    //        }

    //        var acceptedCell = row.Cell(3).Value;
    //        if (!int.TryParse(acceptedCell.ToString(), out var acceptedOrders) || acceptedOrders < 0)
    //            return (false, workingId, shiftDate, null, null, null, null, "Invalid Accepted Orders (must be >= 0)");

    //        var rejectedCell = row.Cell(4).Value;
    //        if (!int.TryParse(rejectedCell.ToString(), out var rejectedOrders) || rejectedOrders < 0)
    //            return (false, workingId, shiftDate, acceptedOrders, null, null, null, "Invalid Rejected Orders (must be >= 0)");

    //        var realRejectedCell = row.Cell(5).Value;
    //        if (!int.TryParse(realRejectedCell.ToString(), out var realRejectedOrders) || realRejectedOrders < 0)
    //            return (false, workingId, shiftDate, acceptedOrders, rejectedOrders, null, null, "Invalid Real Rejected Orders (must be >= 0)");

    //        var hoursCell = row.Cell(6).Value;
    //        if (!float.TryParse(hoursCell.ToString(), out var workingHours) || workingHours < 0 || workingHours > 24)
    //            return (false, workingId, shiftDate, acceptedOrders, rejectedOrders, realRejectedOrders, null, "Invalid Working Hours (must be 0-24)");

    //        return (true, workingId, shiftDate, acceptedOrders, rejectedOrders, realRejectedOrders, workingHours, null);
    //    }
    //    catch (Exception ex)
    //    {
    //        return (false, null, null, null, null, null, null, $"Error: {ex.Message}");
    //    }
    //}


    public async Task<Result<BulkImportResult>> ImportShiftsFromExcelAsync(
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
                return Result.Failure<BulkImportResult>(
                    new Error("InvalidExcel", columnMapping.ErrorMessage!, 400));
            }

            var rows = worksheet.RowsUsed().Skip(1); 
            totalRecords = rows.Count();

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
                    bool isSubstitution = false;

                    if (substitutionDict.TryGetValue(shiftData.WorkingId!.Value, out var substitution))
                    {
                        actualRider = substitution.ActualRider;
                        isSubstitution = true;
                    }
                    else if (ridersByWorkingId.TryGetValue(shiftData.WorkingId!.Value, out var rider))
                    {
                        actualRider = rider;
                    }

                    if (actualRider is null)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"No rider found with working ID {shiftData.WorkingId}"));
                        continue;
                    }

                    var duplicateInBatch = shiftsToAdd.Any(s =>
                        s.RiderId == actualRider.Id &&
                        s.WorkingId == shiftData.WorkingId &&
                        s.ShiftDate == shiftData.ShiftDate);

                    if (duplicateInBatch)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"Duplicate shift in Excel file for date {shiftData.ShiftDate}"));
                        continue;
                    }

                    var shiftStatus = CalculateShiftStatus(
                        shiftData.AcceptedDailyOrders!.Value,
                        actualRider.Company.Name);

                    var (hasRejectionProblem, penaltyAmount) = CalculateRejectionPenalty(
                        shiftData.RealRejectedDailyOrders!.Value);

                    var shift = new RiderShift
                    {
                        RiderId = actualRider.Id,
                        WorkingId = shiftData.WorkingId!.Value,
                        ShiftDate = shiftData.ShiftDate!.Value,
                        AcceptedDailyOrders = shiftData.AcceptedDailyOrders!.Value,
                        RejectedDailyOrders = shiftData.RejectedDailyOrders!.Value,
                        RealRejectedDailyOrders = shiftData.RealRejectedDailyOrders!.Value,
                        WorkingHours = shiftData.WorkingHours!.Value,
                        CompanyId = actualRider.CompanyId,
                        ShiftStatus = shiftStatus.ToString(),
                        CreatedAt = DateTime.UtcNow
                    };

                    shiftsToAdd.Add(shift);

                    if (hasRejectionProblem)
                    {
                        errors.Add(new ImportError(
                            rowNumber,
                            shiftData.WorkingId!.Value.ToString(),
                            $"WARNING: Shift has {shiftData.RealRejectedDailyOrders} rejections (Penalty: {penaltyAmount} SAR)"));
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
                using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    var shiftKeys = shiftsToAdd
                        .Select(s => new { s.RiderId, s.WorkingId, s.ShiftDate })
                        .ToList();

                    var existingShifts = await dbcontext.RiderShifts
                        .Where(s => shiftKeys.Any(k =>
                            k.RiderId == s.RiderId &&
                            k.WorkingId == s.WorkingId &&
                            k.ShiftDate == s.ShiftDate))
                        .Select(s => new { s.RiderId, s.WorkingId, s.ShiftDate })
                        .ToListAsync(cancellationToken);

                    var existingSet = existingShifts.ToHashSet();

                    var newShifts = shiftsToAdd
                        .Where(s => !existingSet.Contains(new { s.RiderId, s.WorkingId, s.ShiftDate }))
                        .ToList();

                    var duplicateCount = shiftsToAdd.Count - newShifts.Count;
                    if (duplicateCount > 0)
                    {
                        errors.Add(new ImportError(
                            0,
                            "Multiple",
                            $"{duplicateCount} shifts already exist in database and were skipped"));
                    }

                    if (newShifts.Any())
                    {
                        await dbcontext.RiderShifts.AddRangeAsync(newShifts, cancellationToken);
                        await dbcontext.SaveChangesAsync(cancellationToken);
                        successCount = newShifts.Count;
                    }

                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result.Failure<BulkImportResult>(
                        new Error("ServerError", $"Database error during bulk insert: {ex.Message}", 500));
                }
            }

            var result = new BulkImportResult(
                totalRecords,
                successCount,
                errors.Count,
                errors
            );

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkImportResult>(
                new Error("ServerError", $"Error reading Excel file: {ex.Message}", 500));
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
        mapping.ShiftDateColumn = FindColumn(headerCells, ExcelColumnConfig.ShiftDateColumns);
        mapping.AcceptedOrdersColumn = FindColumn(headerCells, ExcelColumnConfig.AcceptedOrdersColumns);
        mapping.RejectedOrdersColumn = FindColumn(headerCells, ExcelColumnConfig.RejectedOrdersColumns);
        mapping.RealRejectedOrdersColumn = FindColumn(headerCells, ExcelColumnConfig.RealRejectedOrdersColumns);
        mapping.WorkingHoursColumn = FindColumn(headerCells, ExcelColumnConfig.WorkingHoursColumns);

        var missingColumns = new List<string>();

        if (mapping.WorkingIdColumn == 0)
            missingColumns.Add($"WorkingId (tried: {string.Join(", ", ExcelColumnConfig.WorkingIdColumns)})");
        if (mapping.ShiftDateColumn == 0)
            missingColumns.Add($"ShiftDate (tried: {string.Join(", ", ExcelColumnConfig.ShiftDateColumns)})");
        if (mapping.AcceptedOrdersColumn == 0)
            missingColumns.Add($"AcceptedOrders (tried: {string.Join(", ", ExcelColumnConfig.AcceptedOrdersColumns)})");
        if (mapping.RejectedOrdersColumn == 0)
            missingColumns.Add($"RejectedOrders (tried: {string.Join(", ", ExcelColumnConfig.RejectedOrdersColumns)})");
        if (mapping.RealRejectedOrdersColumn == 0)
            missingColumns.Add($"RealRejectedOrders (tried: {string.Join(", ", ExcelColumnConfig.RealRejectedOrdersColumns)})");
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

    private static (
        bool IsValid,
        int? WorkingId,
        DateOnly? ShiftDate,
        int? AcceptedDailyOrders,
        int? RejectedDailyOrders,
        int? RealRejectedDailyOrders,
        float? WorkingHours,
        string? ErrorMessage) ParseExcelRowByName(IXLRow row, ExcelColumnMapping mapping, int rowNumber)
    {
        try
        {
            var workingIdCell = row.Cell(mapping.WorkingIdColumn).Value;
            if (!int.TryParse(workingIdCell.ToString(), out var workingId))
                return (false, null, null, null, null, null, null, "Invalid Working ID");

            var shiftDateCell = row.Cell(mapping.ShiftDateColumn).Value;
            DateOnly shiftDate;

            if (shiftDateCell.IsDateTime)
            {
                shiftDate = DateOnly.FromDateTime(shiftDateCell.GetDateTime());
            }
            else if (!DateOnly.TryParse(shiftDateCell.ToString(), out shiftDate))
            {
                return (false, workingId, null, null, null, null, null, "Invalid Shift Date");
            }

            var acceptedCell = row.Cell(mapping.AcceptedOrdersColumn).Value;
            if (!int.TryParse(acceptedCell.ToString(), out var acceptedOrders) || acceptedOrders < 0)
                return (false, workingId, shiftDate, null, null, null, null, "Invalid Accepted Orders (must be >= 0)");

            var rejectedCell = row.Cell(mapping.RejectedOrdersColumn).Value;
            if (!int.TryParse(rejectedCell.ToString(), out var rejectedOrders) || rejectedOrders < 0)
                return (false, workingId, shiftDate, acceptedOrders, null, null, null, "Invalid Rejected Orders (must be >= 0)");

            var realRejectedCell = row.Cell(mapping.RealRejectedOrdersColumn).Value;
            if (!int.TryParse(realRejectedCell.ToString(), out var realRejectedOrders) || realRejectedOrders < 0)
                return (false, workingId, shiftDate, acceptedOrders, rejectedOrders, null, null, "Invalid Real Rejected Orders (must be >= 0)");

            var hoursCell = row.Cell(mapping.WorkingHoursColumn).Value;
            if (!float.TryParse(hoursCell.ToString(), out var workingHours) || workingHours < 0 || workingHours > 24)
                return (false, workingId, shiftDate, acceptedOrders, rejectedOrders, realRejectedOrders, null, "Invalid Working Hours (must be 0-24)");

            return (true, workingId, shiftDate, acceptedOrders, rejectedOrders, realRejectedOrders, workingHours, null);
        }
        catch (Exception ex)
        {
            return (false, null, null, null, null, null, null, $"Error: {ex.Message}");
        }
    }



public class ExcelColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public int WorkingIdColumn { get; set; }
    public int ShiftDateColumn { get; set; }
    public int AcceptedOrdersColumn { get; set; }
    public int RejectedOrdersColumn { get; set; }
    public int RealRejectedOrdersColumn { get; set; }
    public int WorkingHoursColumn { get; set; }
}

    public async Task<Result<RiderShiftResponse>> CreateShiftAsync(
       CreateRiderShiftRequest request,
       CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
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

            var (hasRejectionProblem, penaltyAmount) = CalculateRejectionPenalty(
                request.RealRejectedDailyOrders);

            var shift = new RiderShift
            {
                RiderId = actualRiderId,
                WorkingId = request.WorkingId,
                ShiftDate = request.ShiftDate,
                AcceptedDailyOrders = request.AcceptedDailyOrders,
                RejectedDailyOrders = request.RejectedDailyOrders,
                RealRejectedDailyOrders = request.RealRejectedDailyOrders,
                WorkingHours = request.WorkingHours,
                CompanyId = riderDetails.CompanyId,
                ShiftStatus = shiftStatus.ToString(),
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

            if (request.RealRejectedDailyOrders.HasValue)
                shift.RealRejectedDailyOrders = request.RealRejectedDailyOrders.Value;

            if (request.WorkingHours.HasValue)
                shift.WorkingHours = request.WorkingHours.Value;

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
        var shiftStatus = Enum.Parse<ShiftStatus>(shift.ShiftStatus);
        var (hasRejectionProblem, penaltyAmount) = CalculateRejectionPenalty(shift.RealRejectedDailyOrders);
        return new RiderShiftResponse(
            shift.RiderId,
            shift.WorkingId,
            shift.ShiftDate,
            shift.AcceptedDailyOrders,
            shift.RejectedDailyOrders,
            shift.RealRejectedDailyOrders,
            shift.WorkingHours,
            shift.CompanyId,
            shift.Rider.Company.Name,
            shift.Rider.Employee.NameEN,
            shiftStatus,
            hasRejectionProblem,
            penaltyAmount,
            shift.CreatedAt,
            false,
            null
        );
    }


    private static (bool hasRejectionProblem, decimal penaltyAmount) CalculateRejectionPenalty(
           int realRejectedOrders)
    {
        if (realRejectedOrders <= CompanyShiftConfiguration.RejectionThreshold)
            return (false, 0m);

        var excessRejections = realRejectedOrders - CompanyShiftConfiguration.RejectionThreshold;
        var penalty = excessRejections * CompanyShiftConfiguration.PenaltyPerExcessRejection;

        return (true, penalty);
    }

    private static ShiftStatus CalculateShiftStatus(int acceptedOrders, string companyName)
    {
        var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);

        if (acceptedOrders >= dailyTarget)
            return ShiftStatus.Completed;
        else if (acceptedOrders >= (dailyTarget * 0.7)) // 70% of target
            return ShiftStatus.Incomplete;
        else if (acceptedOrders > 0)
            return ShiftStatus.Failed;
        else
            return ShiftStatus.Absent;
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
        { "WorkingId", "Working_ID", "Working ID", "ID", "RiderID", "Rider_ID", "EmployeeID" };

    public static readonly string[] ShiftDateColumns =
        { "ShiftDate", "Shift_Date", "Shift Date", "Date", "WorkDate", "Work_Date" };

    public static readonly string[] AcceptedOrdersColumns =
        { "AcceptedOrders", "Accepted_Orders", "Accepted Orders", "Accepted", "AcceptedDaily", "Accepted_Daily" };

    public static readonly string[] RejectedOrdersColumns =
        { "RejectedOrders", "Rejected_Orders", "Rejected Orders", "Rejected", "RejectedDaily", "Rejected_Daily" };

    public static readonly string[] RealRejectedOrdersColumns =
        { "RealRejectedOrders", "Real_Rejected_Orders", "Real Rejected Orders", "Real Rejected", "ActualRejected", "Actual_Rejected" };

    public static readonly string[] WorkingHoursColumns =
        { "WorkingHours", "Working_Hours", "Working Hours", "Hours", "TotalHours", "Total_Hours" };
}