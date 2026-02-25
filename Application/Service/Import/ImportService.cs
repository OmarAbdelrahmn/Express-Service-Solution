using Application.Abstraction;
using Application.Service.Empolyee;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using static Application.Service.Import.IImportService;

namespace Application.Service.Import;

public class ImportService(ApplicationDbcontext dbcontext, IRiderSub riderSub) : IImportService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;
    private readonly IRiderSub riderSub = riderSub;

    #region Kita Monthly Orders Import (April–December 2025)

    public async Task<Result<KitaMonthlyOrdersImportResponse>> ImportKitaMonthlyOrdersAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<KitaMonthlyOrdersImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<KitaMonthlyOrdersImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<KitaMonthlyEmployeeResult>();
        var globalErrors = new List<string>();

        int employeesFound = 0;
        int employeesNotFound = 0;
        int noRiderDetails = 0;
        int totalCreated = 0;
        int totalUpdated = 0;
        int totalSkipped = 0;
        int errorRows = 0;

        // Days in each month for year 2025 (April = 4 … December = 12)
        var daysInMonth2025 = new Dictionary<int, int>
    {
        { 4,  30 },  // April
        { 5,  31 },  // May
        { 6,  30 },  // June
        { 7,  31 },  // July
        { 8,  31 },  // August
        { 9,  30 },  // September
        { 10, 31 },  // October
        { 11, 30 },  // November
        { 12, 31 }   // December
    };

        try
        {
            Console.WriteLine($"[KitaMonthlyOrders] Starting import for file: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
                return Result.Failure<KitaMonthlyOrdersImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            Console.WriteLine($"[KitaMonthlyOrders] Worksheet loaded: {worksheet.Name}");

            // ── Locate the header row ────────────────────────────────────────
            var headerRow = FindKitaHeaderRow(worksheet);
            if (headerRow == null)
                return Result.Failure<KitaMonthlyOrdersImportResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            Console.WriteLine($"[KitaMonthlyOrders] Header row at row {headerRow.RowNumber()}");

            // ── Build column map ─────────────────────────────────────────────
            var colMap = BuildKitaColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<KitaMonthlyOrdersImportResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            Console.WriteLine($"[KitaMonthlyOrders] Columns mapped. " +
                              $"IqamaCol={colMap.IqamaNoCol}, " +
                              $"MonthCols={string.Join(",", colMap.MonthColumns.Select(m => $"M{m.Key}=C{m.Value}"))}");

            // ── Collect data rows ────────────────────────────────────────────
            var dataRows = worksheet.RowsUsed()
                                      .Where(r => r.RowNumber() > headerRow.RowNumber())
                                      .ToList();
            int totalRows = dataRows.Count;

            Console.WriteLine($"[KitaMonthlyOrders] Data rows: {totalRows}");

            if (totalRows == 0)
                return Result.Failure<KitaMonthlyOrdersImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            // ── Load riders into memory (fast lookup) ────────────────────────
            Console.WriteLine("[KitaMonthlyOrders] Loading rider lookup…");
            // Key = IqamaNo
            var riderLookup = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Select(r => new
                {
                    r.Id,
                    r.EmployeeIqamaNo,
                    r.WorkingId,
                    NameAR = r.Employee.NameAR
                })
                .AsNoTracking()
                .ToListAsync();

            var riderByIqama = riderLookup.ToDictionary(r => r.EmployeeIqamaNo);
            Console.WriteLine($"[KitaMonthlyOrders] Loaded {riderByIqama.Count} riders");

            // ── Load existing shifts for fast duplicate check ─────────────────
            // We'll load per-rider as we go to avoid loading everything upfront.
            // (If dataset is huge, a full load could be done similarly to riderLookup.)

            progressCallback?.Invoke(0, totalRows);

            int processed = 0;
            int rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;
                processed++;

                try
                {
                    // ── Parse IqamaNo ────────────────────────────────────────
                    var iqamaStr = GetCellValue(row, colMap.IqamaNoCol)?.Replace(" ", "").Trim();

                    if (string.IsNullOrWhiteSpace(iqamaStr) ||
                        !long.TryParse(iqamaStr, out long iqamaNo) || iqamaNo <= 0)
                    {
                        errorRows++;
                        results.Add(new KitaMonthlyEmployeeResult(
                            rowNumber, 0,
                            GetCellValue(row, colMap.NameARCol) ?? "N/A",
                            false, null, null,
                            new List<KitaMonthResult>(),
                            $"Invalid or missing IqamaNo: '{iqamaStr}'"
                        ));
                        continue;
                    }

                    string nameAR = GetCellValue(row, colMap.NameARCol)?.Trim() ?? "N/A";

                    // ── Look up rider ────────────────────────────────────────
                    if (!riderByIqama.TryGetValue(iqamaNo, out var riderInfo))
                    {
                        // Try via Employees table (employee exists but may not be a rider)
                        var empExists = await _dbcontext.Employees
                            .AnyAsync(e => e.IqamaNo == iqamaNo);

                        if (empExists)
                        {
                            noRiderDetails++;
                            results.Add(new KitaMonthlyEmployeeResult(
                                rowNumber, iqamaNo, nameAR,
                                true, null, null,
                                new List<KitaMonthResult>(),
                                "Employee found but has no RiderDetails record"
                            ));
                        }
                        else
                        {
                            employeesNotFound++;
                            results.Add(new KitaMonthlyEmployeeResult(
                                rowNumber, iqamaNo, nameAR,
                                false, null, null,
                                new List<KitaMonthResult>(),
                                "Employee not found in system"
                            ));
                        }
                        continue;
                    }

                    employeesFound++;
                    int riderId = riderInfo.Id;
                    string workingId = !string.IsNullOrWhiteSpace(riderInfo.WorkingId)
                                       ? riderInfo.WorkingId
                                       : "0";

                    // ── Load existing shifts for this rider (all 2025 months) ──
                    var existingShifts = await _dbcontext.RiderShifts
                        .Where(s => s.RiderId == riderId &&
                                    s.ShiftDate.Year == 2025 &&
                                    s.ShiftDate.Month >= 4 &&
                                    s.ShiftDate.Month <= 12)
                        .ToListAsync();

                    // Key = ShiftDate for fast lookup
                    var existingShiftMap = existingShifts.ToDictionary(s => s.ShiftDate);

                    var monthResults = new List<KitaMonthResult>();

                    // ── Process each month ────────────────────────────────────
                    foreach (var monthEntry in colMap.MonthColumns)
                    {
                        int month = monthEntry.Key;
                        int colIndex = monthEntry.Value;
                        int daysCount = daysInMonth2025[month];

                        // Read total orders for this month
                        var totalStr = GetCellValue(row, colIndex);
                        if (string.IsNullOrWhiteSpace(totalStr) ||
                            !TryParseInt(totalStr, out int monthTotal))
                        {
                            // Treat unreadable cell as 0 → skip
                            monthResults.Add(new KitaMonthResult(
                                month, 0, daysCount, 0, 0, 0, 0, daysCount, null));
                            totalSkipped += daysCount;
                            continue;
                        }

                        if (monthTotal <= 0)
                        {
                            // Explicitly zero → skip entire month
                            monthResults.Add(new KitaMonthResult(
                                month, 0, daysCount, 0, 0, 0, 0, daysCount, null));
                            totalSkipped += daysCount;
                            continue;
                        }

                        // ── Distribute orders across days ─────────────────────
                        int dailyBase = monthTotal / daysCount;   // floor
                        int remainder = monthTotal % daysCount;   // leftover → goes to last day

                        int monthCreated = 0;
                        int monthUpdated = 0;
                        int monthDaysSkipped = 0;

                        for (int day = 1; day <= daysCount; day++)
                        {
                            // Last day absorbs the remainder
                            int ordersForDay = (day == daysCount)
                                               ? dailyBase + remainder
                                               : dailyBase;

                            if (ordersForDay <= 0)
                            {
                                // This day has no orders → no shift record
                                monthDaysSkipped++;
                                totalSkipped++;
                                continue;
                            }

                            var shiftDate = new DateOnly(2025, month, day);
                            string status = ordersForDay >= 14 ? "completed" : "failed";

                            if (existingShiftMap.TryGetValue(shiftDate, out var existing))
                            {
                                // ── UPDATE existing shift ─────────────────────
                                // NOTE: WorkingId is part of the composite PK — do NOT modify it
                                existing.AcceptedDailyOrders = ordersForDay;
                                existing.RejectedDailyOrders = 0;
                                existing.StackedDeliveries = 0;
                                existing.RealRejectedDailyOrders = 0;
                                existing.WorkingHours = 11;
                                existing.CompanyId = 2;
                                existing.HousingId = null;
                                existing.ShiftStatus = status;

                                monthUpdated++;
                                totalUpdated++;
                            }
                            else
                            {
                                // ── CREATE new shift ──────────────────────────
                                var newShift = new RiderShift
                                {
                                    RiderId = riderId,
                                    WorkingId = workingId,
                                    ShiftDate = shiftDate,
                                    AcceptedDailyOrders = ordersForDay,
                                    RejectedDailyOrders = 0,
                                    StackedDeliveries = 0,
                                    RealRejectedDailyOrders = 0,
                                    HousingId = null,
                                    WorkingHours = 11,
                                    CompanyId = 2,
                                    ShiftStatus = status,
                                    CreatedAt = DateTime.UtcNow.AddHours(3)
                                };

                                await _dbcontext.RiderShifts.AddAsync(newShift);

                                // Keep existingShiftMap in sync so duplicates within same
                                // rider + month don't try to insert twice
                                existingShiftMap[shiftDate] = newShift;

                                monthCreated++;
                                totalCreated++;
                            }
                        }

                        // ── Verify total integrity (defensive check) ──────────
                        // Sum of all orders recorded for this month
                        int recordedTotal = 0;
                        for (int day = 1; day <= daysCount; day++)
                        {
                            int ordersForDay = (day == daysCount)
                                               ? dailyBase + remainder
                                               : dailyBase;
                            if (ordersForDay > 0) recordedTotal += ordersForDay;
                        }

                        string? monthNote = null;
                        if (recordedTotal != monthTotal)
                        {
                            monthNote = $"WARNING: Recorded {recordedTotal} but Excel shows {monthTotal}";
                            globalErrors.Add($"Row {rowNumber} (IqamaNo={iqamaNo}) Month={month}: {monthNote}");
                        }

                        monthResults.Add(new KitaMonthResult(
                            month,
                            monthTotal,
                            daysCount,
                            dailyBase,
                            remainder,
                            monthCreated,
                            monthUpdated,
                            monthDaysSkipped,
                            monthNote
                        ));
                    }

                    // ── Save all changes for this employee in one shot ─────────
                    await _dbcontext.SaveChangesAsync();

                    results.Add(new KitaMonthlyEmployeeResult(
                        rowNumber, iqamaNo, nameAR,
                        true, workingId, riderId,
                        monthResults,
                        null
                    ));

                    Console.WriteLine($"[KitaMonthlyOrders] ✓ Row {rowNumber} | IqamaNo={iqamaNo} | " +
                                      $"WorkingId={workingId} | Created={monthResults.Sum(m => m.ShiftsCreated)} | " +
                                      $"Updated={monthResults.Sum(m => m.ShiftsUpdated)}");
                }
                catch (Exception ex)
                {
                    errorRows++;
                    globalErrors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new KitaMonthlyEmployeeResult(
                        rowNumber, 0, "N/A",
                        false, null, null,
                        new List<KitaMonthResult>(),
                        $"Exception: {ex.Message}"
                    ));

                    Console.WriteLine($"[KitaMonthlyOrders] ERROR Row {rowNumber}: {ex.Message}");
                }

                // Progress callback every 50 rows
                if (processed % 50 == 0)
                {
                    try { progressCallback?.Invoke(processed, totalRows); }
                    catch { /* swallow */ }
                    Console.WriteLine($"[KitaMonthlyOrders] Progress: {processed}/{totalRows}");
                }
            } // end foreach row

            // Final progress
            try { progressCallback?.Invoke(totalRows, totalRows); }
            catch { /* swallow */ }

            Console.WriteLine($"[KitaMonthlyOrders] Import complete:");
            Console.WriteLine($"  Total rows:           {totalRows}");
            Console.WriteLine($"  Employees found:      {employeesFound}");
            Console.WriteLine($"  Employees not found:  {employeesNotFound}");
            Console.WriteLine($"  No rider details:     {noRiderDetails}");
            Console.WriteLine($"  Shifts created:       {totalCreated}");
            Console.WriteLine($"  Shifts updated:       {totalUpdated}");
            Console.WriteLine($"  Days skipped (0 ord): {totalSkipped}");
            Console.WriteLine($"  Error rows:           {errorRows}");

            var response = new KitaMonthlyOrdersImportResponse(
                TotalRowsInExcel: totalRows,
                EmployeesFound: employeesFound,
                EmployeesNotFound: employeesNotFound,
                NoRiderDetails: noRiderDetails,
                TotalShiftsCreated: totalCreated,
                TotalShiftsUpdated: totalUpdated,
                TotalShiftsSkipped: totalSkipped,
                ErrorRows: errorRows,
                Results: results,
                ProcessingErrors: globalErrors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KitaMonthlyOrders] FATAL: {ex}");
            return Result.Failure<KitaMonthlyOrdersImportResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ════════════════════════════════════════════════════════════
    //  HELPER METHODS  (add alongside other private helpers)
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Find the header row that contains 'رقم الإقامة' and at least one numeric month column.
    /// </summary>
    private IXLRow? FindKitaHeaderRow(IXLWorksheet worksheet)
    {
        // Known identifiers in the header
        var iqamaVariants = new[]
        {
        "رقم الإقامة", "رقم الاقامة", "IqamaNumber", "Iqama Number",
        "IqamaNo", "الاقامة", "الإقامة"
    };
        var monthValues = new HashSet<string>
        { "4","5","6","7","8","9","10","11","12" };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cells = row.CellsUsed()
                           .Select(c => c.IsMerged()
                               ? c.MergedRange().FirstCell().GetString().Trim()
                               : c.GetString().Trim())
                           .Where(v => !string.IsNullOrWhiteSpace(v))
                           .ToList();

            bool hasIqama = cells.Any(cv => iqamaVariants.Any(iv =>
                                 cv.Equals(iv, StringComparison.OrdinalIgnoreCase)));
            int monthCount = cells.Count(cv => monthValues.Contains(cv));

            if (hasIqama && monthCount >= 1)
                return row;
        }

        return worksheet.Row(1);
    }

    /// <summary>
    /// Map the Kita Excel header columns to their column numbers.
    /// </summary>
    private KitaColumnMapping BuildKitaColumnMapping(IXLRow headerRow)
    {
        var mapping = new KitaColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        // ── IqamaNo column ──────────────────────────────────────────────────
        mapping.IqamaNoCol = FindColumn(cells,
            "رقم الإقامة", "رقم الاقامة", "IqamaNumber", "Iqama Number",
            "IqamaNo", "Iqama No", "الاقامة", "الإقامة");

        // ── Arabic name column (optional – used for logging) ────────────────
        mapping.NameARCol = FindColumn(cells,
            "اسم الموظف", "اسم الموظف بالعربي", "Name AR", "NameAR", "الاسم");

        // ── Month columns: header cells whose value is "4" … "12" ───────────
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString().Trim()
                    : cell.GetString().Trim();

                if (int.TryParse(val, out int month) && month >= 4 && month <= 12)
                {
                    mapping.MonthColumns[month] = cell.Address.ColumnNumber;
                }
            }
            catch { /* skip bad cells */ }
        }

        // ── Validate ─────────────────────────────────────────────────────────
        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number (رقم الإقامة)");
        if (mapping.MonthColumns.Count == 0) missing.Add("Month columns (4–12)");

        mapping.IsValid = !missing.Any();
        mapping.ErrorMessage = missing.Any()
            ? $"Required columns not found: {string.Join(", ", missing)}"
            : null;

        return mapping;
    }

    // ════════════════════════════════════════════════════════════
    //  INTERNAL CLASSES  (add alongside other internal classes)
    // ════════════════════════════════════════════════════════════

    internal class KitaColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int NameARCol { get; set; }

        // Key = month number (4–12), Value = Excel column number
        public Dictionary<int, int> MonthColumns { get; set; } = new();
    }
    public async Task<Result<IqamaCheckResponse>> CheckIqamasFromExcelAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<IqamaCheckResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<IqamaCheckResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<IqamaCheckRowResult>();
        var errors = new List<string>();

        int foundWithRiderAndWorkingId = 0;
        int foundWithRiderNoWorkingId = 0;
        int foundNoRiderDetails = 0;
        int notFound = 0;
        int failedRecords = 0;

        try
        {
            Console.WriteLine($"[CheckIqamas] Starting check for file: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
                return Result.Failure<IqamaCheckResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            // Find header row
            var headerRow = FindIqamaOnlyHeaderRow(worksheet);
            if (headerRow == null)
                return Result.Failure<IqamaCheckResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));

            // Reuse existing IqamaOnly column mapping
            var columnMap = BuildIqamaOnlyColumnMapping(headerRow);
            if (!columnMap.IsValid)
                return Result.Failure<IqamaCheckResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[CheckIqamas] Total rows: {totalRows}");

            if (totalRows == 0)
                return Result.Failure<IqamaCheckResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            // Load all needed data in bulk for performance
            var allIqamas = new List<long>();
            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;
                var rowData = ParseIqamaOnlyRowData(row, columnMap, rowNumber);
                if (rowData.IsValid && rowData.IqamaNo.HasValue)
                    allIqamas.Add(rowData.IqamaNo.Value);
            }

            // Bulk load employees with rider details
            var employeeLookup = await _dbcontext.Employees
                .Where(e => allIqamas.Contains(e.IqamaNo))
                .Include(e => e.RiderDetails)
                    .ThenInclude(rd => rd!.Company)
                .AsNoTracking()
                .ToDictionaryAsync(e => e.IqamaNo);

            Console.WriteLine($"[CheckIqamas] Loaded {employeeLookup.Count} employees from DB");

            // Process each row
            rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var rowData = ParseIqamaOnlyRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new IqamaCheckRowResult(
                            rowNumber,
                            "N/A",
                            IqamaCheckStatus.ValidationError,
                            null, null, null, null,
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var iqamaNo = rowData.IqamaNo!.Value;

                    if (!employeeLookup.TryGetValue(iqamaNo, out var employee))
                    {
                        notFound++;
                        results.Add(new IqamaCheckRowResult(
                            rowNumber,
                            iqamaNo.ToString(),
                            IqamaCheckStatus.NotFound,
                            null, null, null, null,
                            "Employee not found in system"
                        ));
                        continue;
                    }

                    // Employee found — check rider details
                    if (employee.RiderDetails == null)
                    {
                        foundNoRiderDetails++;
                        results.Add(new IqamaCheckRowResult(
                            rowNumber,
                            iqamaNo.ToString(),
                            IqamaCheckStatus.FoundNoRiderDetails,
                            employee.NameEN,
                            employee.NameAR,
                            null,
                            null,
                            "Employee exists but has no RiderDetails"
                        ));
                        continue;
                    }

                    // Has rider details — check working ID
                    bool hasWorkingId = !string.IsNullOrWhiteSpace(employee.RiderDetails.WorkingId);

                    if (hasWorkingId)
                    {
                        foundWithRiderAndWorkingId++;
                        results.Add(new IqamaCheckRowResult(
                            rowNumber,
                            iqamaNo.ToString(),
                            IqamaCheckStatus.FoundWithRiderAndWorkingId,
                            employee.NameEN,
                            employee.NameAR,
                            employee.RiderDetails.WorkingId,
                            employee.RiderDetails.Company?.Name,
                            null
                        ));
                    }
                    else
                    {
                        foundWithRiderNoWorkingId++;
                        results.Add(new IqamaCheckRowResult(
                            rowNumber,
                            iqamaNo.ToString(),
                            IqamaCheckStatus.FoundWithRiderNoWorkingId,
                            employee.NameEN,
                            employee.NameAR,
                            null,
                            employee.RiderDetails.Company?.Name,
                            "Has RiderDetails but WorkingId is empty"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");
                    results.Add(new IqamaCheckRowResult(
                        rowNumber,
                        "N/A",
                        IqamaCheckStatus.ValidationError,
                        null, null, null, null,
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            Console.WriteLine($"[CheckIqamas] Complete:");
            Console.WriteLine($"  - Total: {totalRows}");
            Console.WriteLine($"  - Found (Rider + WorkingId): {foundWithRiderAndWorkingId}");
            Console.WriteLine($"  - Found (Rider, No WorkingId): {foundWithRiderNoWorkingId}");
            Console.WriteLine($"  - Found (No Rider): {foundNoRiderDetails}");
            Console.WriteLine($"  - Not Found: {notFound}");
            Console.WriteLine($"  - Failed: {failedRecords}");

            var response = new IqamaCheckResponse(
                TotalRecords: totalRows,
                FoundWithRiderAndWorkingId: foundWithRiderAndWorkingId,
                FoundWithRiderNoWorkingId: foundWithRiderNoWorkingId,
                FoundNoRiderDetails: foundNoRiderDetails,
                NotFound: notFound,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CheckIqamas] FATAL ERROR: {ex}");
            return Result.Failure<IqamaCheckResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }
    public async Task<Result<CompanyTransferImportResponse>> TransferRidersByIqamaAsync(
        IFormFile file,
        int newCompanyId
        )
    {
        if (file == null || file.Length == 0)
            return Result.Failure<CompanyTransferImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<CompanyTransferImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<CompanyTransferRowResult>();
        var errors = new List<string>();
        int successfulTransfers = 0;
        int failedRecords = 0;
        int employeeNotFound = 0;
        int riderDetailsNotFound = 0;
        int companyNotFound = 0;

        try
        {
            // Verify company exists
            var newCompany = await _dbcontext.Companies
                .FirstOrDefaultAsync(c => c.Id == newCompanyId);

            if (newCompany == null)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("CompanyNotFound", $"Company with ID {newCompanyId} not found", 404));
            }

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindIqamaOnlyHeaderRow(worksheet);
            if (headerRow == null)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildIqamaOnlyColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseIqamaOnlyRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new CompanyTransferRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            null,
                            null,
                            newCompanyId,
                            null,
                            null,
                            newCompany.Name,
                            null,
                            null,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Find employee with rider details
                    var employee = await _dbcontext.Employees
                        .Include(e => e.RiderDetails)
                            .ThenInclude(rd => rd!.Company)
                        .FirstOrDefaultAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (employee == null)
                    {
                        employeeNotFound++;
                        failedRecords++;
                        results.Add(new CompanyTransferRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            null,
                            null,
                            newCompanyId,
                            null,
                            null,
                            newCompany.Name,
                            null,
                            null,
                            warnings,
                            "Employee with this Iqama number not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    if (employee.RiderDetails == null)
                    {
                        riderDetailsNotFound++;
                        failedRecords++;
                        results.Add(new CompanyTransferRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            null,
                            null,
                            newCompanyId,
                            null,
                            null,
                            newCompany.Name,
                            employee.NameEN,
                            employee.NameAR,
                            warnings,
                            "Employee exists but has no RiderDetails record"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Store old values
                    string? currentWorkingId = employee.RiderDetails.WorkingId;
                    int oldCompanyId = employee.RiderDetails.CompanyId;
                    string? oldCompanyName = employee.RiderDetails.Company?.Name;

                    // Check if already in target company
                    if (oldCompanyId == newCompanyId)
                    {
                        warnings.Add($"Already in company '{newCompany.Name}'");
                    }

                    // Deactivate old WorkingId histories
                    var oldHistories = await _dbcontext.RiderWorkingIdHistories
                        .Where(h => h.RiderIqamaNo == employee.IqamaNo && h.IsActive)
                        .ToListAsync();

                    var now = DateTime.UtcNow.AddHours(3);

                    foreach (var oldHistory in oldHistories)
                    {
                        oldHistory.IsActive = false;
                        oldHistory.EndDate = now;
                    }

                    // Update CompanyId ONLY (keep WorkingId the same)
                    employee.RiderDetails.CompanyId = newCompanyId;

                    // Add new WorkingId history with EXISTING WorkingId
                    var newHistory = new RiderWorkingIdHistory
                    {
                        RiderIqamaNo = employee.IqamaNo,
                        WorkingId = currentWorkingId ?? $"AUTO_{employee.IqamaNo}",
                        CompanyId = newCompanyId,
                        StartDate = now,
                        IsActive = true,
                        Notes = $"Transferred to {newCompany.Name} by omar via bulk import (WorkingId unchanged)"
                    };

                    await _dbcontext.RiderWorkingIdHistories.AddAsync(newHistory);

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulTransfers++;
                    results.Add(new CompanyTransferRowResult(
                        rowNumber,
                        true,
                        rowData.IqamaNo!.Value.ToString(),
                        currentWorkingId, // Same as old WorkingId
                        currentWorkingId, // Old WorkingId (unchanged)
                        newCompanyId,
                        oldCompanyId,
                        oldCompanyName,
                        newCompany.Name,
                        employee.NameEN,
                        employee.NameAR,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new CompanyTransferRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        null,
                        newCompanyId,
                        null,
                        null,
                        newCompany.Name,
                        null,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new CompanyTransferImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulTransfers: successfulTransfers,
                FailedRecords: failedRecords,
                EmployeeNotFound: employeeNotFound,
                RiderDetailsNotFound: riderDetailsNotFound,
                CompanyNotFound: companyNotFound,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<CompanyTransferImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // Helper methods
    private IXLRow? FindIqamaOnlyHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة",
        "IqamaNo", "Iqama No", "Iqama", "الاقامة"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 1)
                return row;
        }

        return worksheet.Row(1);
    }

    private IqamaOnlyColumnMapping BuildIqamaOnlyColumnMapping(IXLRow headerRow)
    {
        var mapping = new IqamaOnlyColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "رقم الاقامة", "رقم الإقامة", "Iqama", "الاقامة");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required column missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private IqamaOnlyRowData ParseIqamaOnlyRowData(
        IXLRow row,
        IqamaOnlyColumnMapping map,
        int rowNumber)
    {
        var data = new IqamaOnlyRowData { RowNumber = rowNumber };

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // Internal classes
    internal class IqamaOnlyColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
    }

    internal class IqamaOnlyRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
    }
    public async Task<Result<SparePartQuantityUpdateResponse>> UpdateSparePartQuantitiesAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<SparePartQuantityUpdateResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<SparePartQuantityUpdateResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<SparePartQuantityUpdateRowResult>();
        var errors = new List<string>();
        int successfulUpdates = 0;
        int noChangeNeeded = 0;
        int sparePartNotFound = 0;
        int failedRecords = 0;

        try
        {
            Console.WriteLine($"[UpdateSparePartQuantities] Starting update for file: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[UpdateSparePartQuantities] ERROR: Could not read worksheet");
                return Result.Failure<SparePartQuantityUpdateResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[UpdateSparePartQuantities] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindSparePartQuantityUpdateHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[UpdateSparePartQuantities] ERROR: No header row found");
                return Result.Failure<SparePartQuantityUpdateResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[UpdateSparePartQuantities] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildSparePartQuantityUpdateColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[UpdateSparePartQuantities] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<SparePartQuantityUpdateResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine($"[UpdateSparePartQuantities] Column mapping successful");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[UpdateSparePartQuantities] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[UpdateSparePartQuantities] WARNING: No data rows found");
                return Result.Failure<SparePartQuantityUpdateResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseSparePartQuantityUpdateRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new SparePartQuantityUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.Name ?? "N/A",
                            null,
                            rowData.Quantity,
                            false,
                            rowData.ErrorMessage
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Find spare part by name (case-insensitive)
                    var sparePart = await _dbcontext.SpareParts
                        .FirstOrDefaultAsync(sp => sp.Name.ToLower() == rowData.Name!.ToLower());

                    if (sparePart == null)
                    {
                        sparePartNotFound++;
                        failedRecords++;
                        results.Add(new SparePartQuantityUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.Name!,
                            null,
                            rowData.Quantity,
                            false,
                            $"Spare part '{rowData.Name}' not found in database"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    int oldQuantity = sparePart.Quantity;

                    // Check if update is needed
                    if (oldQuantity == rowData.Quantity)
                    {
                        noChangeNeeded++;
                        results.Add(new SparePartQuantityUpdateRowResult(
                            rowNumber,
                            true,
                            sparePart.Name,
                            oldQuantity,
                            rowData.Quantity,
                            false,
                            null
                        ));
                        await transaction.CommitAsync();
                        Console.WriteLine($"[UpdateSparePartQuantities] No change needed for '{sparePart.Name}' - Quantity already {oldQuantity}");
                        continue;
                    }

                    // Update quantity
                    sparePart.Quantity = rowData.Quantity;

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulUpdates++;
                    results.Add(new SparePartQuantityUpdateRowResult(
                        rowNumber,
                        true,
                        sparePart.Name,
                        oldQuantity,
                        rowData.Quantity,
                        true,
                        null
                    ));

                    Console.WriteLine($"[UpdateSparePartQuantities] ✓ Updated '{sparePart.Name}': {oldQuantity} → {rowData.Quantity}");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new SparePartQuantityUpdateRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        0,
                        false,
                        $"Exception: {ex.Message}"
                    ));

                    Console.WriteLine($"[UpdateSparePartQuantities] ERROR at row {rowNumber}: {ex.Message}");
                }
            }

            Console.WriteLine($"[UpdateSparePartQuantities] Update complete:");
            Console.WriteLine($"  - Total: {totalRows}");
            Console.WriteLine($"  - Successful Updates: {successfulUpdates}");
            Console.WriteLine($"  - No Change Needed: {noChangeNeeded}");
            Console.WriteLine($"  - Not Found: {sparePartNotFound}");
            Console.WriteLine($"  - Failed: {failedRecords}");

            var response = new SparePartQuantityUpdateResponse(
                TotalRecords: totalRows,
                SuccessfulUpdates: successfulUpdates,
                NoChangeNeeded: noChangeNeeded,
                SparePartNotFound: sparePartNotFound,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateSparePartQuantities] FATAL ERROR: {ex}");
            return Result.Failure<SparePartQuantityUpdateResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    private IXLRow? FindSparePartQuantityUpdateHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "Name", "الاسم", "Part Name", "Spare Part Name", "اسم القطعة",
        "Quantity", "الكمية", "Qty", "Stock", "المخزون"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private SparePartQuantityUpdateColumnMapping BuildSparePartQuantityUpdateColumnMapping(IXLRow headerRow)
    {
        var mapping = new SparePartQuantityUpdateColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.NameCol = FindColumn(cells,
            "Name", "الاسم", "Part Name", "Spare Part Name", "اسم القطعة");

        mapping.QuantityCol = FindColumn(cells,
            "Quantity", "الكمية", "Qty", "Stock", "المخزون");

        var missing = new List<string>();
        if (mapping.NameCol == 0) missing.Add("Name");
        if (mapping.QuantityCol == 0) missing.Add("Quantity");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private SparePartQuantityUpdateRowData ParseSparePartQuantityUpdateRowData(
        IXLRow row,
        SparePartQuantityUpdateColumnMapping map,
        int rowNumber)
    {
        var data = new SparePartQuantityUpdateRowData { RowNumber = rowNumber };

        try
        {
            data.Name = GetCellValue(row, map.NameCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.Name))
            {
                data.IsValid = false;
                data.ErrorMessage = "Spare part name is required";
                return data;
            }

            var quantityStr = GetCellValue(row, map.QuantityCol);
            if (string.IsNullOrWhiteSpace(quantityStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Quantity is required";
                return data;
            }

            if (!TryParseInt(quantityStr, out int quantity))
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid quantity: '{quantityStr}'";
                return data;
            }

            if (quantity < 0)
            {
                data.IsValid = false;
                data.ErrorMessage = "Quantity cannot be negative";
                return data;
            }

            data.Quantity = quantity;
            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ============================================
    // INTERNAL CLASSES
    // ============================================

    internal class SparePartQuantityUpdateColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int NameCol { get; set; }
        public int QuantityCol { get; set; }
    }

    internal class SparePartQuantityUpdateRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
    }
    public async Task<Result<CompanyTransferImportResponse>> TransferRidersToCompanyAsync(
    IFormFile file,
    int newCompanyId,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<CompanyTransferImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<CompanyTransferImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<CompanyTransferRowResult>();
        var errors = new List<string>();
        int successfulTransfers = 0;
        int failedRecords = 0;
        int employeeNotFound = 0;
        int riderDetailsNotFound = 0;
        int companyNotFound = 0;

        try
        {
            // Verify company exists
            var newCompany = await _dbcontext.Companies
                .FirstOrDefaultAsync(c => c.Id == newCompanyId);

            if (newCompany == null)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("CompanyNotFound", $"Company with ID {newCompanyId} not found", 404));
            }

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindCompanyTransferHeaderRow(worksheet);
            if (headerRow == null)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildCompanyTransferColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<CompanyTransferImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseCompanyTransferRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new CompanyTransferRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            rowData.NewWorkingId,
                            null,
                            newCompanyId,
                            null,
                            null,
                            newCompany.Name,
                            null,
                            null,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Find employee with rider details
                    var employee = await _dbcontext.Employees
                        .Include(e => e.RiderDetails)
                            .ThenInclude(rd => rd!.Company)
                        .FirstOrDefaultAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (employee == null)
                    {
                        employeeNotFound++;
                        failedRecords++;
                        results.Add(new CompanyTransferRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NewWorkingId,
                            null,
                            newCompanyId,
                            null,
                            null,
                            newCompany.Name,
                            null,
                            null,
                            warnings,
                            "Employee with this Iqama number not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    if (employee.RiderDetails == null)
                    {
                        riderDetailsNotFound++;
                        failedRecords++;
                        results.Add(new CompanyTransferRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NewWorkingId,
                            null,
                            newCompanyId,
                            null,
                            null,
                            newCompany.Name,
                            employee.NameEN,
                            employee.NameAR,
                            warnings,
                            "Employee exists but has no RiderDetails record"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Store old values
                    string? oldWorkingId = employee.RiderDetails.WorkingId;
                    int oldCompanyId = employee.RiderDetails.CompanyId;
                    string? oldCompanyName = employee.RiderDetails.Company?.Name;

                    // Check if already in target company
                    if (oldCompanyId == newCompanyId)
                    {
                        warnings.Add($"Already in company '{newCompany.Name}'");
                    }

                    // Deactivate old WorkingId histories
                    var oldHistories = await _dbcontext.RiderWorkingIdHistories
                        .Where(h => h.RiderIqamaNo == employee.IqamaNo && h.IsActive)
                        .ToListAsync();

                    var now = DateTime.UtcNow.AddHours(3);

                    foreach (var oldHistory in oldHistories)
                    {
                        oldHistory.IsActive = false;
                        oldHistory.EndDate = now;
                    }

                    // Update RiderDetails
                    employee.RiderDetails.WorkingId = rowData.NewWorkingId;
                    employee.RiderDetails.CompanyId = newCompanyId;

                    // Add new WorkingId history
                    var newHistory = new RiderWorkingIdHistory
                    {
                        RiderIqamaNo = employee.IqamaNo,
                        WorkingId = rowData.NewWorkingId!,
                        CompanyId = newCompanyId,
                        StartDate = now,
                        IsActive = true,
                        Notes = $"Transferred to {newCompany.Name} by {uploadedBy} via bulk import"
                    };

                    await _dbcontext.RiderWorkingIdHistories.AddAsync(newHistory);

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulTransfers++;
                    results.Add(new CompanyTransferRowResult(
                        rowNumber,
                        true,
                        rowData.IqamaNo!.Value.ToString(),
                        rowData.NewWorkingId,
                        oldWorkingId,
                        newCompanyId,
                        oldCompanyId,
                        oldCompanyName,
                        newCompany.Name,
                        employee.NameEN,
                        employee.NameAR,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new CompanyTransferRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        null,
                        newCompanyId,
                        null,
                        null,
                        newCompany.Name,
                        null,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new CompanyTransferImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulTransfers: successfulTransfers,
                FailedRecords: failedRecords,
                EmployeeNotFound: employeeNotFound,
                RiderDetailsNotFound: riderDetailsNotFound,
                CompanyNotFound: companyNotFound,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<CompanyTransferImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // Helper methods
    private IXLRow? FindCompanyTransferHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة",
        "WorkingId", "Working ID", "معرف العمل", "رقم العمل"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private CompanyTransferColumnMapping BuildCompanyTransferColumnMapping(IXLRow headerRow)
    {
        var mapping = new CompanyTransferColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "رقم الاقامة", "رقم الإقامة");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working ID", "WorkingID", "معرف العمل", "رقم العمل");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.WorkingIdCol == 0) missing.Add("Working ID");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private CompanyTransferRowData ParseCompanyTransferRowData(
        IXLRow row,
        CompanyTransferColumnMapping map,
        int rowNumber)
    {
        var data = new CompanyTransferRowData { RowNumber = rowNumber };

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            data.NewWorkingId = GetCellValue(row, map.WorkingIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.NewWorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "Working ID is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // Internal classes
    internal class CompanyTransferColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int WorkingIdCol { get; set; }
    }

    internal class CompanyTransferRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
        public string? NewWorkingId { get; set; }
    }

    public async Task<Result<SubstitutionImportResponse>> SyncSubstitutionsFromExcelAsync(
    IFormFile file,
    string uploadedBy,
    CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<SubstitutionImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<SubstitutionImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var details = new List<SubstitutionImportDetail>();
        var errors = new List<string>();

        int activeSubstitutionsCreated = 0;
        int activeSubstitutionsRetained = 0;
        int activeSubstitutionsStopped = 0;
        int validationErrors = 0;
        int actualRiderNotFound = 0;
        int substituteRiderNotFound = 0;

        try
        {
            Console.WriteLine($"[SubstitutionImport] Starting sync for file: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[SubstitutionImport] ERROR: Could not read worksheet");
                return Result.Failure<SubstitutionImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[SubstitutionImport] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindSubstitutionHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[SubstitutionImport] ERROR: No header row found");
                return Result.Failure<SubstitutionImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[SubstitutionImport] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildSubstitutionColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[SubstitutionImport] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<SubstitutionImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine($"[SubstitutionImport] Column mapping successful");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[SubstitutionImport] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[SubstitutionImport] WARNING: No data rows found");
                return Result.Failure<SubstitutionImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            // STEP 1: Parse all valid rows from Excel
            var excelSubstitutions = new HashSet<(string ActualWorkingId, string SubstituteWorkingId)>(
                EqualityComparer<(string ActualWorkingId, string SubstituteWorkingId)>.Create(
                    (x, y) =>
                        string.Equals(x.ActualWorkingId, y.ActualWorkingId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.SubstituteWorkingId, y.SubstituteWorkingId, StringComparison.OrdinalIgnoreCase),
                    obj =>
                        (obj.ActualWorkingId?.ToLowerInvariant().GetHashCode() ?? 0) ^
                        (obj.SubstituteWorkingId?.ToLowerInvariant().GetHashCode() ?? 0)
                )
            );
            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var rowData = ParseSubstitutionRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        validationErrors++;
                        details.Add(new SubstitutionImportDetail(
                            rowNumber,
                            rowData.ActualRiderWorkingId ?? "N/A",
                            rowData.SubstituteWorkingId ?? "N/A",
                            SubstitutionImportStatus.ValidationError,
                            null,
                            null,
                            null,
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    // Add to Excel substitutions set (case-insensitive)
                    var key = (rowData.ActualRiderWorkingId!.Trim(), rowData.SubstituteWorkingId!.Trim());
                    excelSubstitutions.Add(key);
                }
                catch (Exception ex)
                {
                    validationErrors++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    details.Add(new SubstitutionImportDetail(
                        rowNumber,
                        "N/A",
                        "N/A",
                        SubstitutionImportStatus.ValidationError,
                        null,
                        null,
                        null,
                        $"Processing error: {ex.Message}"
                    ));
                }
            }

            Console.WriteLine($"[SubstitutionImport] Valid substitutions in Excel: {excelSubstitutions.Count}");

            // STEP 2: Get all currently active substitutions from database
            var currentActiveSubstitutions = await _dbcontext.RiderShiftSubstitutions
                .Where(s => s.IsActive)
                .ToListAsync(cancellationToken);

            Console.WriteLine($"[SubstitutionImport] Current active substitutions in DB: {currentActiveSubstitutions.Count}");

            // STEP 3: Stop substitutions that are NOT in Excel
            foreach (var current in currentActiveSubstitutions)
            {
                var key = (current.ActualRiderWorkingId.Trim(), current.SubstituteWorkingId.Trim());

                // If this substitution is NOT in Excel, stop it
                if (!excelSubstitutions.Contains(key))
                {
                    Console.WriteLine($"[SubstitutionImport] Stopping substitution: {current.ActualRiderWorkingId} -> {current.SubstituteWorkingId} (not in Excel)");

                    var stopResult = await riderSub.StopSubstitutionByWorkingId(
                        current.ActualRiderWorkingId,
                        cancellationToken);

                    if (stopResult.IsSuccess)
                    {
                        activeSubstitutionsStopped++;
                        Console.WriteLine($"[SubstitutionImport] ✓ Stopped substitution for {current.ActualRiderWorkingId}");
                    }
                    else
                    {
                        errors.Add($"Failed to stop substitution {current.ActualRiderWorkingId} -> {current.SubstituteWorkingId}: {stopResult.Error.Description}");
                    }
                }
            }

            // STEP 4: Process each substitution from Excel
            rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                var rowData = ParseSubstitutionRowData(row, columnMap, rowNumber);

                if (!rowData.IsValid)
                    continue; // Already handled in STEP 1

                try
                {
                    var actualWorkingId = rowData.ActualRiderWorkingId!.Trim();
                    var substituteWorkingId = rowData.SubstituteWorkingId!.Trim();

                    // Check if this substitution already exists and is active
                    var existingSubstitution = await _dbcontext.RiderShiftSubstitutions
                        .Include(s => s.ActualRider)
                            .ThenInclude(r => r.Employee)
                        .Include(s => s.SubstituteRider)
                            .ThenInclude(r => r.Employee)
                        .FirstOrDefaultAsync(s =>
                            s.ActualRiderWorkingId == actualWorkingId &&
                            s.SubstituteWorkingId == substituteWorkingId &&
                            s.IsActive,
                            cancellationToken);

                    if (existingSubstitution != null)
                    {
                        // Already exists - retain it
                        activeSubstitutionsRetained++;

                        var actualRiderName = existingSubstitution.ActualRider?.Employee?.NameEN
                            ?? $"Unassigned WorkingId [{actualWorkingId}]";
                        var substituteRiderName = existingSubstitution.SubstituteRider.Employee.NameEN;

                        details.Add(new SubstitutionImportDetail(
                            rowNumber,
                            actualWorkingId,
                            substituteWorkingId,
                            SubstitutionImportStatus.Retained,
                            "Already active - retained",
                            actualRiderName,
                            substituteRiderName,
                            null
                        ));

                        Console.WriteLine($"[SubstitutionImport] ✓ Retained existing: {actualWorkingId} -> {substituteWorkingId}");
                        continue;
                    }

                    // Create new substitution using the existing service
                    var request = new StartSubstitutionRequest(
                        ActualRiderWorkingId: actualWorkingId,
                        SubstituteWorkingId: substituteWorkingId,
                        Reason: uploadedBy,
                        CreatedBy: uploadedBy
                    );

                    var createResult = await riderSub.StartSubstitution(request, cancellationToken);

                    if (createResult.IsSuccess)
                    {
                        activeSubstitutionsCreated++;

                        details.Add(new SubstitutionImportDetail(
                            rowNumber,
                            actualWorkingId,
                            substituteWorkingId,
                            SubstitutionImportStatus.Created,
                            "New substitution created",
                            createResult.Value.ActualRiderName,
                            createResult.Value.SubstituteRiderName,
                            null
                        ));

                        Console.WriteLine($"[SubstitutionImport] ✓ Created: {actualWorkingId} -> {substituteWorkingId}");
                    }
                    else
                    {
                        // Determine error type
                        var errorMessage = createResult.Error.Description;
                        SubstitutionImportStatus status;

                        if (errorMessage.Contains("Substitute rider not found"))
                        {
                            status = SubstitutionImportStatus.SubstituteRiderNotFound;
                            substituteRiderNotFound++;
                        }
                        else if (errorMessage.Contains("not found") || errorMessage.Contains("NotFound"))
                        {
                            status = SubstitutionImportStatus.ActualRiderNotFound;
                            actualRiderNotFound++;
                        }
                        else
                        {
                            status = SubstitutionImportStatus.ValidationError;
                            validationErrors++;
                        }

                        details.Add(new SubstitutionImportDetail(
                            rowNumber,
                            actualWorkingId,
                            substituteWorkingId,
                            status,
                            null,
                            null,
                            null,
                            errorMessage
                        ));

                        errors.Add($"Row {rowNumber}: {errorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    validationErrors++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    details.Add(new SubstitutionImportDetail(
                        rowNumber,
                        rowData.ActualRiderWorkingId ?? "N/A",
                        rowData.SubstituteWorkingId ?? "N/A",
                        SubstitutionImportStatus.ValidationError,
                        null,
                        null,
                        null,
                        $"Exception: {ex.InnerException.Message}"
                    ));
                }
            }

            Console.WriteLine($"[SubstitutionImport] Sync complete:");
            Console.WriteLine($"  - Total in Excel: {totalRows}");
            Console.WriteLine($"  - Created: {activeSubstitutionsCreated}");
            Console.WriteLine($"  - Retained: {activeSubstitutionsRetained}");
            Console.WriteLine($"  - Stopped (not in Excel): {activeSubstitutionsStopped}");
            Console.WriteLine($"  - Validation Errors: {validationErrors}");

            var response = new SubstitutionImportResponse(
                TotalRecordsInExcel: totalRows,
                ActiveSubstitutionsCreated: activeSubstitutionsCreated,
                ActiveSubstitutionsRetained: activeSubstitutionsRetained,
                ActiveSubstitutionsStopped: activeSubstitutionsStopped,
                ValidationErrors: validationErrors,
                ActualRiderNotFound: actualRiderNotFound,
                SubstituteRiderNotFound: substituteRiderNotFound,
                Details: details,
                ProcessingErrors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SubstitutionImport] FATAL ERROR: {ex}");
            return Result.Failure<SubstitutionImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    private IXLRow? FindSubstitutionHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
            "ActualRiderWorkingId", "Actual Rider", "Main Working ID", "معرف الأساسي",
            "SubstituteWorkingId", "Substitute Rider", "Sub Working ID", "معرف البديل",
            "MainWorkingId", "SubWorkingId", "Original", "Replacement"
        };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private SubstitutionColumnMapping BuildSubstitutionColumnMapping(IXLRow headerRow)
    {
        var mapping = new SubstitutionColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.ActualRiderWorkingIdCol = FindColumn(cells,
            "ActualRiderWorkingId", "Actual Rider", "Main Working ID", "MainWorkingId",
            "معرف الأساسي", "الأساسي", "Original", "ActualWorkingId");

        mapping.SubstituteWorkingIdCol = FindColumn(cells,
            "SubstituteWorkingId", "Substitute Rider", "Sub Working ID", "SubWorkingId",
            "معرف البديل", "البديل", "Replacement", "SubstituteWorkingId");

        var missing = new List<string>();
        if (mapping.ActualRiderWorkingIdCol == 0) missing.Add("ActualRiderWorkingId / Main Working ID");
        if (mapping.SubstituteWorkingIdCol == 0) missing.Add("SubstituteWorkingId / Sub Working ID");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;

    }
    private SubstitutionRowData ParseSubstitutionRowData(
        IXLRow row,
        SubstitutionColumnMapping map,
        int rowNumber)
    {
        var data = new SubstitutionRowData { RowNumber = rowNumber };

        try
        {
            data.ActualRiderWorkingId = GetCellValue(row, map.ActualRiderWorkingIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.ActualRiderWorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "Actual Rider Working ID is required";
                return data;
            }

            data.SubstituteWorkingId = GetCellValue(row, map.SubstituteWorkingIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.SubstituteWorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "Substitute Working ID is required";
                return data;
            }

            // Check if they're the same
            if (data.ActualRiderWorkingId.Equals(data.SubstituteWorkingId, StringComparison.OrdinalIgnoreCase))
            {
                data.IsValid = false;
                data.ErrorMessage = "Actual and Substitute Working IDs cannot be the same";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    internal class SubstitutionColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int ActualRiderWorkingIdCol { get; set; }
        public int SubstituteWorkingIdCol { get; set; }
    }

    internal class SubstitutionRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ActualRiderWorkingId { get; set; }
        public string? SubstituteWorkingId { get; set; }
    }


    // Add this method to ImportService.cs class
    public async Task<Result<SparePartImportResponse>> ImportSparePartsAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<SparePartImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<SparePartImportResponse>(
                new Error("InvalidFormat", "File must be Excel format", 400));

        var results = new List<SparePartImportRowResult>();
        var errors = new List<string>();
        int successfulImports = 0;
        int updatedRecords = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
                return Result.Failure<SparePartImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindSparePartHeaderRow(worksheet);
            if (headerRow == null)
                return Result.Failure<SparePartImportResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            var columnMap = BuildSparePartColumnMapping(headerRow);
            if (!columnMap.IsValid)
                return Result.Failure<SparePartImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseSparePartRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new SparePartImportRowResult(
                            rowNumber, false,
                            rowData.Name ?? "N/A",
                            rowData.Quantity,
                            rowData.Price,
                            rowData.Location ?? "N/A",
                            false, false,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Check if spare part exists
                    var existing = await _dbcontext.SpareParts
                        .FirstOrDefaultAsync(sp => sp.Name.ToLower() == rowData.Name!.ToLower());

                    bool created = false;
                    bool updated = false;

                    if (existing == null)
                    {
                        var sparePart = new Domain.Entities.Spare.SparePart
                        {
                            Name = rowData.Name!,
                            Quantity = rowData.Quantity,
                            Price = rowData.Price,
                            Location = rowData.Location!,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        await _dbcontext.SpareParts.AddAsync(sparePart);
                        created = true;
                        successfulImports++;
                    }
                    else
                    {
                        existing.Quantity += rowData.Quantity;
                        existing.Price = rowData.Price;
                        existing.Location = rowData.Location!;

                        updated = true;
                        updatedRecords++;
                        warnings.Add($"Updated existing part, quantity increased by {rowData.Quantity}");
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    results.Add(new SparePartImportRowResult(
                        rowNumber, true,
                        rowData.Name!,
                        rowData.Quantity,
                        rowData.Price,
                        rowData.Location!,
                        created, updated,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new SparePartImportRowResult(
                        rowNumber, false,
                        "N/A", 0, 0, "N/A",
                        false, false,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new SparePartImportResponse(
                dataRows.Count,
                successfulImports,
                updatedRecords,
                failedRecords,
                results,
                errors,
                DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<SparePartImportResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    public async Task<Result<RiderAccessoryImportResponse>> ImportRiderAccessoriesAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<RiderAccessoryImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<RiderAccessoryImportResponse>(
                new Error("InvalidFormat", "File must be Excel format", 400));

        var results = new List<RiderAccessoryImportRowResult>();
        var errors = new List<string>();
        int successfulImports = 0;
        int updatedRecords = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
                return Result.Failure<RiderAccessoryImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            var headerRow = FindAccessoryHeaderRow(worksheet);
            if (headerRow == null)
                return Result.Failure<RiderAccessoryImportResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            var columnMap = BuildAccessoryColumnMapping(headerRow);
            if (!columnMap.IsValid)
                return Result.Failure<RiderAccessoryImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseAccessoryRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new RiderAccessoryImportRowResult(
                            rowNumber, false,
                            rowData.Name ?? "N/A",
                            rowData.Quantity,
                            rowData.Price,
                            rowData.Location ?? "N/A",
                            false, false,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Check if accessory exists
                    var existing = await _dbcontext.RiderAccessories
                        .FirstOrDefaultAsync(a =>
                            a.Name.ToLower() == rowData.Name!.ToLower());

                    bool created = false;
                    bool updated = false;

                    if (existing == null)
                    {
                        var accessory = new Domain.Entities.Spare.RiderAccessory
                        {
                            Name = rowData.Name!,
                            Quantity = rowData.Quantity,
                            Price = rowData.Price,
                            Location = rowData.Location!,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        await _dbcontext.RiderAccessories.AddAsync(accessory);
                        created = true;
                        successfulImports++;
                    }
                    else
                    {
                        existing.Quantity += rowData.Quantity;
                        existing.Price = rowData.Price;
                        existing.Location = rowData.Location!;

                        updated = true;
                        updatedRecords++;
                        warnings.Add($"Updated existing accessory, quantity increased by {rowData.Quantity}");
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    results.Add(new RiderAccessoryImportRowResult(
                        rowNumber, true,
                        rowData.Name!,
                        rowData.Quantity,
                        rowData.Price,
                        rowData.Location!,
                        created, updated,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new RiderAccessoryImportRowResult(
                        rowNumber, false,
                        "N/A", 0, 0, "N/A",
                        false, false,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new RiderAccessoryImportResponse(
                dataRows.Count,
                successfulImports,
                updatedRecords,
                failedRecords,
                results,
                errors,
                DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderAccessoryImportResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    // Helper methods
    private IXLRow? FindSparePartHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[] { "Name", "الاسم", "Quantity", "الكمية", "Price", "السعر", "Location", "الموقع" };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed().Select(c => c.GetString().Trim()).ToList();

            if (cellValues.Count(cv => knownColumns.Any(kc =>
                cv.Equals(kc, StringComparison.OrdinalIgnoreCase))) >= 3)
                return row;
        }

        return worksheet.Row(1);
    }

    // ─── 1) BuildSparePartColumnMapping ────────────────────────────
    private SparePartColumnMapping BuildSparePartColumnMapping(IXLRow headerRow)
    {
        var mapping = new SparePartColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.NameCol = FindColumn(cells, "Name", "الاسم", "Part Name");
        // These columns are no longer required — keep finding them so existing
        // files with extra columns don't break, but we won't use the values.
        mapping.QuantityCol = FindColumn(cells, "Quantity", "الكمية", "Qty");
        mapping.PriceCol = FindColumn(cells, "Price", "السعر", "Cost");
        mapping.LocationCol = FindColumn(cells, "Location", "الموقع", "Storage");

        // Only Name is required now
        var missing = new List<string>();
        if (mapping.NameCol == 0) missing.Add("Name");

        mapping.IsValid = !missing.Any();
        mapping.ErrorMessage = missing.Any() ? $"Missing: {string.Join(", ", missing)}" : null;

        return mapping;
    }


    // ─── 2) ParseSparePartRowData ──────────────────────────────────
    private SparePartRowData ParseSparePartRowData(IXLRow row, SparePartColumnMapping map, int rowNumber)
    {
        var data = new SparePartRowData { RowNumber = rowNumber };

        try
        {
            data.Name = GetCellValue(row, map.NameCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.Name))
            {
                data.IsValid = false;
                data.ErrorMessage = "Name is required";
                return data;
            }

            // Hard-coded values — ignore whatever is in the Excel columns
            data.Quantity = 0;
            data.Price = 0m;
            data.Location = "الشركة";

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }


    // ─── 3) BuildAccessoryColumnMapping ────────────────────────────
    private AccessoryColumnMapping BuildAccessoryColumnMapping(IXLRow headerRow)
    {
        var mapping = new AccessoryColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.NameCol = FindColumn(cells, "Name", "الاسم", "Accessory Name");
        // Keep finding these so existing files don't error on extra columns,
        // but we won't use their values any more.
        mapping.TypeCol = FindColumn(cells, "Type", "النوع", "Category");
        mapping.QuantityCol = FindColumn(cells, "Quantity", "الكمية", "Qty");
        mapping.PriceCol = FindColumn(cells, "Price", "السعر", "Cost");
        mapping.LocationCol = FindColumn(cells, "Location", "الموقع", "Storage");

        // Only Name is required now
        var missing = new List<string>();
        if (mapping.NameCol == 0) missing.Add("Name");

        mapping.IsValid = !missing.Any();
        mapping.ErrorMessage = missing.Any() ? $"Missing: {string.Join(", ", missing)}" : null;

        return mapping;
    }


    // ─── 4) ParseAccessoryRowData ──────────────────────────────────
    private AccessoryRowData ParseAccessoryRowData(IXLRow row, AccessoryColumnMapping map, int rowNumber)
    {
        var data = new AccessoryRowData { RowNumber = rowNumber };

        try
        {
            data.Name = GetCellValue(row, map.NameCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.Name))
            {
                data.IsValid = false;
                data.ErrorMessage = "Name is required";
                return data;
            }

            // Hard-coded values — ignore whatever is in the Excel columns
            data.Quantity = 0;
            data.Price = 0m;
            data.Location = "الشركة";

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }




    private IXLRow? FindAccessoryHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[] { "Name", "الاسم", "Type", "النوع", "Quantity", "الكمية", "Price", "السعر" };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed().Select(c => c.GetString().Trim()).ToList();

            if (cellValues.Count(cv => knownColumns.Any(kc =>
                cv.Equals(kc, StringComparison.OrdinalIgnoreCase))) >= 3)
                return row;
        }

        return worksheet.Row(1);
    }



    // Internal classes
    internal class SparePartColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int NameCol { get; set; }
        public int QuantityCol { get; set; }
        public int PriceCol { get; set; }
        public int LocationCol { get; set; }
        public int VehicleTypeCol { get; set; }
    }

    internal class SparePartRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Location { get; set; }
        public string? VehicleType { get; set; }
    }

    internal class AccessoryColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int NameCol { get; set; }
        public int TypeCol { get; set; }
        public int QuantityCol { get; set; }
        public int PriceCol { get; set; }
        public int LocationCol { get; set; }
    }

    internal class AccessoryRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Location { get; set; }
    }
    public async Task<Result<VehicleRelocationImportResponse>> ImportVehicleRelocationsAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleRelocationImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleRelocationImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleRelocationRowResult>();
        var errors = new List<string>();
        int successfulRelocations = 0;
        int locationUpdated = 0;
        int statusUpdated = 0;
        int failedRecords = 0;
        int vehicleNotFound = 0;
        int housingNotFound = 0;

        try
        {
            Console.WriteLine($"[VehicleRelocation] Starting import for file: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[VehicleRelocation] ERROR: Could not read worksheet");
                return Result.Failure<VehicleRelocationImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[VehicleRelocation] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindVehicleRelocationHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[VehicleRelocation] ERROR: No header row found");
                return Result.Failure<VehicleRelocationImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[VehicleRelocation] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildVehicleRelocationColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[VehicleRelocation] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<VehicleRelocationImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine("[VehicleRelocation] Column mapping successful");

            // Load housing lookup dictionary
            var housings = await _dbcontext.Housings
                .AsNoTracking()
                .ToDictionaryAsync(h => h.Id, h => h.Name);

            Console.WriteLine($"[VehicleRelocation] Loaded {housings.Count} housing entries");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[VehicleRelocation] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[VehicleRelocation] WARNING: No data rows found");
                return Result.Failure<VehicleRelocationImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseVehicleRelocationRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new VehicleRelocationRowResult(
                            rowNumber,
                            false,
                            rowData.PlateNumber ?? "N/A",
                            "N/A",
                            "N/A",
                            false,
                            false,
                            null,
                            null,
                            null,
                            null,
                            rowData.Reason,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    var warnings = new List<string>();

                    // Normalize plate number
                    var normalizedPlate = rowData.PlateNumber!.Replace(" ", "").Trim();

                    // Find vehicle
                    var vehicle = await _dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.PlateNumberA.Replace(" ", "") == normalizedPlate);

                    if (vehicle == null)
                    {
                        vehicleNotFound++;
                        failedRecords++;
                        results.Add(new VehicleRelocationRowResult(
                            rowNumber,
                            false,
                            rowData.PlateNumber!,
                            "N/A",
                            "N/A",
                            false,
                            false,
                            null,
                            null,
                            null,
                            null,
                            rowData.Reason,
                            warnings,
                            $"Vehicle with plate '{rowData.PlateNumber}' not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    bool locationChanged = false;
                    bool statusChanged = false;
                    string? oldLocation = vehicle.Location;
                    string? newLocation = null;
                    string? oldStatus = null;
                    string? newStatus = null;

                    // Get current status
                    var currentActiveStatus = await _dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == vehicle.VehicleNumber && s.IsActive)
                        .OrderByDescending(s => s.Timestamp)
                        .FirstOrDefaultAsync();

                    oldStatus = currentActiveStatus?.StatusType.ToString() ?? "Returned";

                    // 1. Update Location if HousingId provided
                    if (rowData.HousingId.HasValue)
                    {
                        if (!housings.TryGetValue(rowData.HousingId.Value, out string? housingName))
                        {
                            housingNotFound++;
                            failedRecords++;
                            results.Add(new VehicleRelocationRowResult(
                                rowNumber,
                                false,
                                rowData.PlateNumber!,
                                vehicle.VehicleNumber,
                                vehicle.VehicleType,
                                false,
                                false,
                                oldLocation,
                                null,
                                oldStatus,
                                null,
                                rowData.Reason,
                                warnings,
                                $"Housing with ID {rowData.HousingId.Value} not found"
                            ));
                            await transaction.RollbackAsync();
                            continue;
                        }

                        newLocation = housingName;

                        if (vehicle.Location != newLocation)
                        {
                            vehicle.Location = newLocation;
                            locationChanged = true;
                            warnings.Add($"Location changed from '{oldLocation}' to '{newLocation}'");
                        }
                    }
                    else
                    {
                        // No housing specified - set to company
                        newLocation = "الشركة";

                        if (vehicle.Location != newLocation)
                        {
                            vehicle.Location = newLocation;
                            locationChanged = true;
                            warnings.Add($"Location changed from '{oldLocation}' to '{newLocation}'");
                        }
                    }

                    // 2. Update Status if provided
                    if (!string.IsNullOrWhiteSpace(rowData.NewStatus))
                    {
                        newStatus = rowData.NewStatus;

                        // Validate status value
                        var validStatuses = new[] { "Available", "Problem", "Stolen", "BreakUp", "Returned" };
                        if (!validStatuses.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
                        {
                            warnings.Add($"Invalid status '{newStatus}' - using 'Available'");
                            newStatus = "Available";
                        }

                        if (!oldStatus.Equals(newStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            // Map status string to enum
                            VehicleStatusType newStatusType = newStatus.ToLower() switch
                            {
                                "2" => VehicleStatusType.Returned,
                                "3" => VehicleStatusType.Problem,
                                "stolen" => VehicleStatusType.Stolen,
                                "breakup" => VehicleStatusType.BreakUp,
                                _ => VehicleStatusType.Returned
                            };

                            // Deactivate old status
                            if (currentActiveStatus != null)
                            {
                                currentActiveStatus.IsActive = false;
                                currentActiveStatus.PermissionEndDate = DateTime.UtcNow.AddHours(3);
                            }

                            // Create new status record
                            var isActive = newStatusType != VehicleStatusType.Returned;

                            _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                            {
                                VehicleNumber = vehicle.VehicleNumber,
                                EmployeeIqamaNo = null,
                                StatusType = newStatusType,
                                Reason = rowData.Reason ?? $"Status updated via bulk import by {uploadedBy}",
                                IsActive = isActive,
                                Timestamp = DateTime.UtcNow.AddHours(3),
                                PermissionEndDate = DateTime.UtcNow.AddHours(3)
                            });

                            statusChanged = true;
                            warnings.Add($"Status changed from '{oldStatus}' to '{newStatus}'");

                            // If status is Available/Returned, clear any rider assignment
                            if (newStatusType == VehicleStatusType.Returned)
                            {
                                var rider = await _dbcontext.RiderDetails
                                    .FirstOrDefaultAsync(r => r.VehicleNumber == vehicle.VehicleNumber);

                                if (rider != null)
                                {
                                    rider.VehicleNumber = null;
                                    warnings.Add("Vehicle assignment cleared from rider");
                                }
                            }
                        }
                    }

                    // Check if any changes were made
                    if (!locationChanged && !statusChanged)
                    {
                        warnings.Add("No changes detected - location and status are already correct");
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    if (locationChanged || statusChanged)
                    {
                        successfulRelocations++;
                        if (locationChanged) locationUpdated++;
                        if (statusChanged) statusUpdated++;
                    }

                    results.Add(new VehicleRelocationRowResult(
                        rowNumber,
                        true,
                        rowData.PlateNumber!,
                        vehicle.VehicleNumber,
                        vehicle.VehicleType,
                        locationChanged,
                        statusChanged,
                        oldLocation,
                        newLocation,
                        oldStatus,
                        newStatus,
                        rowData.Reason,
                        warnings,
                        null
                    ));

                    Console.WriteLine($"[VehicleRelocation] ✓ Processed {vehicle.VehicleNumber} - Location: {locationChanged}, Status: {statusChanged}");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new VehicleRelocationRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        "N/A",
                        "N/A",
                        false,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            Console.WriteLine($"[VehicleRelocation] Import complete:");
            Console.WriteLine($"  - Total: {totalRows}");
            Console.WriteLine($"  - Successful: {successfulRelocations}");
            Console.WriteLine($"  - Location Updated: {locationUpdated}");
            Console.WriteLine($"  - Status Updated: {statusUpdated}");
            Console.WriteLine($"  - Failed: {failedRecords}");

            var response = new VehicleRelocationImportResponse(
                TotalRecords: totalRows,
                SuccessfulRelocations: successfulRelocations,
                LocationUpdated: locationUpdated,
                StatusUpdated: statusUpdated,
                FailedRecords: failedRecords,
                VehicleNotFound: vehicleNotFound,
                HousingNotFound: housingNotFound,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VehicleRelocation] FATAL ERROR: {ex}");
            return Result.Failure<VehicleRelocationImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // ============================================
    // HELPER METHODS FOR VEHICLE RELOCATION
    // ============================================

    private IXLRow? FindVehicleRelocationHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "plateNo", "Plate Number", "PlateNumberA", "رقم اللوحة",
        "HousingId", "Housing ID", "رقم السكن",
        "NewStatus", "Status", "الحالة",
        "Reason", "السبب", "ملاحظات"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 1) // At least PlateNumber must be found
                return row;
        }

        return worksheet.Row(1);
    }

    private VehicleRelocationColumnMapping BuildVehicleRelocationColumnMapping(IXLRow headerRow)
    {
        var mapping = new VehicleRelocationColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.PlateNumberCol = FindColumn(cells,
            "plateNo", "Plate Number", "PlateNumberA", "Plate Number A",
            "رقم اللوحة", "اللوحة", "اللوحة العربية");

        mapping.HousingIdCol = FindColumn(cells,
            "housingId", "Housing ID", "Housing Id", "HousingID",
            "رقم السكن", "السكن", "معرف السكن");

        mapping.NewStatusCol = FindColumn(cells,
            "NewStatus", "status", "New Status", "CurrentStatus",
            "الحالة", "الحالة الجديدة", "حالة المركبة");

        mapping.ReasonCol = FindColumn(cells,
            "Reason", "السبب", "ملاحظات", "Notes", "الملاحظات");

        var missing = new List<string>();
        if (mapping.PlateNumberCol == 0) missing.Add("PlateNumber");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required column missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private VehicleRelocationRowData ParseVehicleRelocationRowData(
        IXLRow row,
        VehicleRelocationColumnMapping map,
        int rowNumber)
    {
        var data = new VehicleRelocationRowData { RowNumber = rowNumber };

        try
        {
            // Parse PlateNumber (REQUIRED)
            data.PlateNumber = GetCellValue(row, map.PlateNumberCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.PlateNumber))
            {
                data.IsValid = false;
                data.ErrorMessage = "PlateNumber is required";
                return data;
            }

            // Parse HousingId (OPTIONAL)
            var housingIdStr = GetCellValue(row, map.HousingIdCol);
            if (!string.IsNullOrWhiteSpace(housingIdStr) && TryParseInt(housingIdStr, out int housingId))
            {
                data.HousingId = housingId;
            }

            // Parse NewStatus (OPTIONAL)
            data.NewStatus = GetCellValue(row, map.NewStatusCol)?.Trim();

            // Parse Reason (OPTIONAL)
            data.Reason = GetCellValue(row, map.ReasonCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.Reason))
            {
                data.Reason = "Bulk relocation from Excel import";
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ============================================
    // INTERNAL CLASSES FOR VEHICLE RELOCATION
    // ============================================

    internal class VehicleRelocationColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int PlateNumberCol { get; set; }
        public int HousingIdCol { get; set; }
        public int NewStatusCol { get; set; }
        public int ReasonCol { get; set; }
    }

    internal class VehicleRelocationRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PlateNumber { get; set; }
        public int? HousingId { get; set; }
        public string? NewStatus { get; set; }
        public string? Reason { get; set; }
    }

    public async Task<Result<RiderVerificationResponse>> VerifyRidersFromExcelAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<RiderVerificationResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<RiderVerificationResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var details = new List<RiderVerificationDetail>();
        var errors = new List<string>();

        int fullyMatched = 0;
        int workingIdFoundNameMismatch = 0; // Count but don't add to details
        int nameFoundWorkingIdMismatch = 0;
        int completelyNotFound = 0;
        int errorRecords = 0;

        // Track unique WorkingId errors to avoid duplicates
        var reportedWorkingIdErrors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workingIdErrorSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Console.WriteLine($"[VerifyRiders] Starting verification for file: {file.FileName}");

            // Load all rider data into memory
            Console.WriteLine("[VerifyRiders] Loading rider details lookup...");
            var riderDetailsLookup = await LoadRiderDetailsLookup();
            Console.WriteLine($"[VerifyRiders] Loaded {riderDetailsLookup.Count} rider detail entries");

            Console.WriteLine("[VerifyRiders] Loading working ID history lookup...");
            var workingIdHistoryLookup = await LoadWorkingIdHistoryLookup();
            Console.WriteLine($"[VerifyRiders] Loaded {workingIdHistoryLookup.Count} working ID history entries");

            using var stream = file.OpenReadStream();
            Console.WriteLine($"[VerifyRiders] File stream opened, length: {stream.Length} bytes");

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[VerifyRiders] ERROR: Could not read worksheet");
                return Result.Failure<RiderVerificationResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[VerifyRiders] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindRiderVerificationHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[VerifyRiders] ERROR: No header row found");
                return Result.Failure<RiderVerificationResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[VerifyRiders] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildRiderVerificationColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[VerifyRiders] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<RiderVerificationResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine($"[VerifyRiders] Column mapping successful - WorkingId: Col {columnMap.WorkingIdCol}, NameAR: Col {columnMap.NameARCol}");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[VerifyRiders] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[VerifyRiders] WARNING: No data rows found");
                return Result.Failure<RiderVerificationResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            // Report initial total
            try
            {
                progressCallback?.Invoke(0, totalRows);
                Console.WriteLine($"[VerifyRiders] Initial progress callback sent: 0/{totalRows}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VerifyRiders] ERROR in progress callback: {ex.Message}");
            }

            var rowNumber = headerRow.RowNumber();
            int processedCount = 0;

            foreach (var row in dataRows)
            {
                rowNumber++;
                processedCount++;

                try
                {
                    var rowData = ParseRiderVerificationRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        errorRecords++;
                        details.Add(new RiderVerificationDetail(
                            rowNumber,
                            rowData.WorkingId ?? "N/A",
                            rowData.NameAR ?? "N/A",
                            VerificationStatus.ValidationError,
                            null, null, null, null,
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var verification = VerifyRiderData(
                        rowData.WorkingId!,
                        rowData.NameAR!,
                        riderDetailsLookup,
                        workingIdHistoryLookup
                    );

                    switch (verification.Status)
                    {
                        case VerificationStatus.FullyMatched:
                            fullyMatched++;
                            break;

                        case VerificationStatus.WorkingIdFoundNameMismatch:
                            // COUNT IT but DON'T add to details (ignore name mismatches)
                            workingIdFoundNameMismatch++;
                            break;

                        case VerificationStatus.NameFoundWorkingIdMismatch:
                            nameFoundWorkingIdMismatch++;

                            // Only report this WorkingId error ONCE
                            var workingId = rowData.WorkingId!.Trim().ToLower();

                            if (!reportedWorkingIdErrors.Contains(workingId))
                            {
                                reportedWorkingIdErrors.Add(workingId);

                                details.Add(new RiderVerificationDetail(
                                    rowNumber,
                                    rowData.WorkingId!,
                                    rowData.NameAR!,
                                    verification.Status,
                                    verification.FoundInTable,
                                    verification.ActualWorkingId,
                                    verification.ActualNameAR,
                                    verification.FoundIqamaNo,
                                    $"Name exists but WorkingId mismatch: Expected '{rowData.WorkingId}', Found '{verification.ActualWorkingId}'"
                                ));
                            }

                            // Track count for summary
                            if (!workingIdErrorSummary.ContainsKey(workingId))
                            {
                                workingIdErrorSummary[workingId] = 0;
                            }
                            workingIdErrorSummary[workingId]++;
                            break;

                        case VerificationStatus.CompletelyNotFound:
                            completelyNotFound++;

                            // Only report this WorkingId error ONCE
                            var notFoundWorkingId = rowData.WorkingId!.Trim().ToLower();

                            if (!reportedWorkingIdErrors.Contains(notFoundWorkingId))
                            {
                                reportedWorkingIdErrors.Add(notFoundWorkingId);

                                details.Add(new RiderVerificationDetail(
                                    rowNumber,
                                    rowData.WorkingId!,
                                    rowData.NameAR!,
                                    verification.Status,
                                    null, null, null, null,
                                    "Neither WorkingId nor NameAR found in system"
                                ));
                            }

                            // Track count for summary
                            if (!workingIdErrorSummary.ContainsKey(notFoundWorkingId))
                            {
                                workingIdErrorSummary[notFoundWorkingId] = 0;
                            }
                            workingIdErrorSummary[notFoundWorkingId]++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errorRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");
                    details.Add(new RiderVerificationDetail(
                        rowNumber,
                        "N/A", "N/A",
                        VerificationStatus.ValidationError,
                        null, null, null, null,
                        $"Processing error: {ex.Message}"
                    ));
                }

                // Report progress every 500 rows
                if (processedCount % 500 == 0)
                {
                    try
                    {
                        progressCallback?.Invoke(processedCount, totalRows);
                        Console.WriteLine($"[VerifyRiders] Progress: {processedCount}/{totalRows} ({(processedCount * 100.0 / totalRows):F1}%)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VerifyRiders] ERROR in progress callback at row {processedCount}: {ex.Message}");
                    }
                }
            }

            // Final progress update
            try
            {
                progressCallback?.Invoke(totalRows, totalRows);
                Console.WriteLine($"[VerifyRiders] Final progress callback sent: {totalRows}/{totalRows}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VerifyRiders] ERROR in final progress callback: {ex.Message}");
            }

            // Add summary of duplicate errors to processing errors
            if (workingIdErrorSummary.Any())
            {
                errors.Add("=== DUPLICATE ERROR SUMMARY ===");
                foreach (var kvp in workingIdErrorSummary.OrderByDescending(x => x.Value))
                {
                    errors.Add($"WorkingId '{kvp.Key}' appeared {kvp.Value} times with errors");
                }
            }

            Console.WriteLine($"[VerifyRiders] Verification complete:");
            Console.WriteLine($"  - Total: {totalRows}");
            Console.WriteLine($"  - Fully Matched: {fullyMatched}");
            Console.WriteLine($"  - Name Mismatch (Ignored): {workingIdFoundNameMismatch}");
            Console.WriteLine($"  - WorkingId Mismatch: {nameFoundWorkingIdMismatch}");
            Console.WriteLine($"  - Not Found: {completelyNotFound}");
            Console.WriteLine($"  - Errors: {errorRecords}");
            Console.WriteLine($"  - Unique WorkingId Errors Reported: {reportedWorkingIdErrors.Count}");
            Console.WriteLine($"  - Total WorkingId Issues (including duplicates): {workingIdErrorSummary.Values.Sum()}");

            var response = new RiderVerificationResponse(
                TotalRecordsProcessed: totalRows,
                FullyMatched: fullyMatched,
                WorkingIdFoundNameMismatch: workingIdFoundNameMismatch, // Count but not in details
                NameFoundWorkingIdMismatch: nameFoundWorkingIdMismatch,
                CompletelyNotFound: completelyNotFound,
                ErrorRecords: errorRecords,
                Details: details, // Only unique WorkingId errors
                ProcessingErrors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VerifyRiders] FATAL ERROR: {ex}");
            return Result.Failure<RiderVerificationResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }
    // Helper Methods
    // ===========================

    // Add this method to ImportService.cs

    public async Task<Result<WorkingIdSyncResponse>> SyncWorkingIdsFromExcelAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<WorkingIdSyncResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<WorkingIdSyncResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var details = new List<WorkingIdSyncDetail>();
        var errors = new List<string>();

        int workingIdHistoriesAdded = 0;
        int riderDetailsCreated = 0;
        int alreadyCorrect = 0;
        int nameNotFound = 0;
        int duplicatesSkipped = 0;
        int errorRecords = 0;

        // Track processed WorkingIds to skip duplicates
        var processedWorkingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Console.WriteLine($"[WorkingIdSync] Starting sync for file: {file.FileName}");

            // Load all employee and rider data
            Console.WriteLine("[WorkingIdSync] Loading employee lookup...");
            var employeeLookup = await LoadEmployeeLookupByName();
            Console.WriteLine($"[WorkingIdSync] Loaded {employeeLookup.Count} employee entries");

            Console.WriteLine("[WorkingIdSync] Loading rider details lookup...");
            var riderDetailsLookup = await LoadRiderDetailsByIqama();
            Console.WriteLine($"[WorkingIdSync] Loaded {riderDetailsLookup.Count} rider detail entries");

            // Load default company for creating new RiderDetails
            var defaultCompany = await _dbcontext.Companies
                .Where(c => c.Id == 1)
                .FirstOrDefaultAsync();

            if (defaultCompany == null)
            {
                return Result.Failure<WorkingIdSyncResponse>(
                    new Error("NoCompany", "No company found in database for creating RiderDetails", 400));
            }

            using var stream = file.OpenReadStream();
            Console.WriteLine($"[WorkingIdSync] File stream opened, length: {stream.Length} bytes");

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[WorkingIdSync] ERROR: Could not read worksheet");
                return Result.Failure<WorkingIdSyncResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[WorkingIdSync] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindRiderVerificationHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[WorkingIdSync] ERROR: No header row found");
                return Result.Failure<WorkingIdSyncResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[WorkingIdSync] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildRiderVerificationColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[WorkingIdSync] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<WorkingIdSyncResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine($"[WorkingIdSync] Column mapping successful - WorkingId: Col {columnMap.WorkingIdCol}, NameAR: Col {columnMap.NameARCol}");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[WorkingIdSync] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[WorkingIdSync] WARNING: No data rows found");
                return Result.Failure<WorkingIdSyncResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            // Report initial total
            try
            {
                progressCallback?.Invoke(0, totalRows);
                Console.WriteLine($"[WorkingIdSync] Initial progress callback sent: 0/{totalRows}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorkingIdSync] ERROR in progress callback: {ex.Message}");
            }

            var rowNumber = headerRow.RowNumber();
            int processedCount = 0;

            foreach (var row in dataRows)
            {
                rowNumber++;
                processedCount++;

                try
                {
                    var rowData = ParseRiderVerificationRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        errorRecords++;
                        details.Add(new WorkingIdSyncDetail(
                            rowNumber,
                            rowData.WorkingId ?? "N/A",
                            rowData.NameAR ?? "N/A",
                            SyncStatus.ValidationError,
                            null,
                            null,
                            null,
                            null,
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    // Check for duplicate WorkingId in this Excel file
                    var workingIdKey = rowData.WorkingId!.Trim().ToLower();

                    if (processedWorkingIds.Contains(workingIdKey))
                    {
                        // This is a duplicate - skip it
                        duplicatesSkipped++;

                        // Track count for summary
                        if (!duplicateSummary.ContainsKey(workingIdKey))
                        {
                            duplicateSummary[workingIdKey] = 1; // First occurrence was processed
                        }
                        duplicateSummary[workingIdKey]++;

                        details.Add(new WorkingIdSyncDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.NameAR!,
                            SyncStatus.DuplicateSkipped,
                            $"Duplicate WorkingId '{rowData.WorkingId}' - only first occurrence processed",
                            null,
                            null,
                            null,
                            $"WorkingId '{rowData.WorkingId}' appears multiple times in Excel"
                        ));
                        continue;
                    }

                    // Mark this WorkingId as processed
                    processedWorkingIds.Add(workingIdKey);

                    // Find employee by Arabic name
                    var nameKey = rowData.NameAR!.Trim().ToLower();

                    if (!employeeLookup.TryGetValue(nameKey, out var employeeInfo))
                    {
                        nameNotFound++;
                        details.Add(new WorkingIdSyncDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.NameAR!,
                            SyncStatus.NameNotFound,
                            null,
                            null,
                            null,
                            null,
                            "Employee with this Arabic name not found in system"
                        ));
                        continue;
                    }

                    // Check if employee has RiderDetails
                    if (!riderDetailsLookup.TryGetValue(employeeInfo.IqamaNo, out var riderDetails))
                    {
                        // Create RiderDetails for this employee
                        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

                        try
                        {
                            var newRiderDetails = new RiderDetails
                            {
                                EmployeeIqamaNo = employeeInfo.IqamaNo,
                                WorkingId = rowData.WorkingId,
                                TshirtSize = "M",
                                LicenseNumber = "N/A",
                                CompanyId = defaultCompany.Id,
                                CreatedAt = DateTime.UtcNow.AddHours(3)
                            };

                            await _dbcontext.RiderDetails.AddAsync(newRiderDetails);

                            // Add to WorkingId history
                            var historyRecord = new RiderWorkingIdHistory
                            {
                                RiderIqamaNo = employeeInfo.IqamaNo,
                                WorkingId = rowData.WorkingId!,
                                CompanyId = defaultCompany.Id,
                                StartDate = DateTime.UtcNow.AddHours(3),
                                IsActive = false,
                                Notes = $"Created by WorkingId sync import - {uploadedBy}"
                            };

                            await _dbcontext.RiderWorkingIdHistories.AddAsync(historyRecord);

                            await _dbcontext.SaveChangesAsync();
                            await transaction.CommitAsync();

                            riderDetailsCreated++;

                            details.Add(new WorkingIdSyncDetail(
                                rowNumber,
                                rowData.WorkingId!,
                                rowData.NameAR!,
                                SyncStatus.RiderDetailsCreated,
                                "Created RiderDetails and added WorkingId to history",
                                employeeInfo.IqamaNo,
                                rowData.WorkingId,
                                defaultCompany.Name,
                                null
                            ));

                            // Update local lookup
                            riderDetailsLookup[employeeInfo.IqamaNo] = new RiderDetailsInfo
                            {
                                IqamaNo = employeeInfo.IqamaNo,
                                WorkingId = rowData.WorkingId,
                                CompanyId = defaultCompany.Id,
                                CompanyName = defaultCompany.Name
                            };
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            errorRecords++;
                            errors.Add($"Row {rowNumber}: Failed to create RiderDetails - {ex.Message}");

                            details.Add(new WorkingIdSyncDetail(
                                rowNumber,
                                rowData.WorkingId!,
                                rowData.NameAR!,
                                SyncStatus.ValidationError,
                                null,
                                employeeInfo.IqamaNo,
                                null,
                                null,
                                $"Failed to create RiderDetails: {ex.Message}"
                            ));
                        }

                        continue;
                    }

                    // RiderDetails exists - check if WorkingId needs update
                    if (riderDetails.WorkingId == rowData.WorkingId)
                    {
                        alreadyCorrect++;
                        details.Add(new WorkingIdSyncDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.NameAR!,
                            SyncStatus.AlreadyCorrect,
                            "WorkingId already matches",
                            employeeInfo.IqamaNo,
                            riderDetails.WorkingId,
                            riderDetails.CompanyName,
                            null
                        ));
                        continue;
                    }

                    // WorkingId is different - add to history and update RiderDetails
                    using var updateTransaction = await _dbcontext.Database.BeginTransactionAsync();

                    try
                    {
                        // Deactivate old WorkingId histories
                        var oldHistories = await _dbcontext.RiderWorkingIdHistories
                            .Where(h => h.RiderIqamaNo == employeeInfo.IqamaNo && h.IsActive)
                            .ToListAsync();

                        var now = DateTime.UtcNow.AddHours(3);

                        foreach (var oldHistory in oldHistories)
                        {
                            oldHistory.IsActive = false;
                            oldHistory.EndDate = now;
                        }

                        // Add new history record
                        var newHistory = new RiderWorkingIdHistory
                        {
                            RiderIqamaNo = employeeInfo.IqamaNo,
                            WorkingId = rowData.WorkingId!,
                            CompanyId = riderDetails.CompanyId,
                            StartDate = now,
                            IsActive = true,
                            Notes = $"Updated by WorkingId sync import - {uploadedBy}"
                        };

                        await _dbcontext.RiderWorkingIdHistories.AddAsync(newHistory);

                        // Update RiderDetails
                        var riderDetailsEntity = await _dbcontext.RiderDetails
                            .FirstOrDefaultAsync(rd => rd.EmployeeIqamaNo == employeeInfo.IqamaNo);

                        if (riderDetailsEntity != null)
                        {
                            riderDetailsEntity.WorkingId = rowData.WorkingId;
                        }

                        await _dbcontext.SaveChangesAsync();
                        await updateTransaction.CommitAsync();

                        workingIdHistoriesAdded++;

                        details.Add(new WorkingIdSyncDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.NameAR!,
                            SyncStatus.HistoryAdded,
                            $"Updated WorkingId from '{riderDetails.WorkingId}' to '{rowData.WorkingId}'",
                            employeeInfo.IqamaNo,
                            rowData.WorkingId,
                            riderDetails.CompanyName,
                            null
                        ));

                        // Update local lookup
                        riderDetailsLookup[employeeInfo.IqamaNo] = riderDetailsLookup[employeeInfo.IqamaNo] with
                        {
                            WorkingId = rowData.WorkingId
                        };
                    }
                    catch (Exception ex)
                    {
                        await updateTransaction.RollbackAsync();
                        errorRecords++;
                        errors.Add($"Row {rowNumber}: Failed to update WorkingId - {ex.Message}");

                        details.Add(new WorkingIdSyncDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.NameAR!,
                            SyncStatus.ValidationError,
                            null,
                            employeeInfo.IqamaNo,
                            riderDetails.WorkingId,
                            riderDetails.CompanyName,
                            $"Failed to update: {ex.Message}"
                        ));
                    }
                }
                catch (Exception ex)
                {
                    errorRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");
                    details.Add(new WorkingIdSyncDetail(
                        rowNumber,
                        "N/A", "N/A",
                        SyncStatus.ValidationError,
                        null,
                        null,
                        null,
                        null,
                        $"Processing error: {ex.Message}"
                    ));
                }

                // Report progress every 500 rows
                if (processedCount % 500 == 0)
                {
                    try
                    {
                        progressCallback?.Invoke(processedCount, totalRows);
                        Console.WriteLine($"[WorkingIdSync] Progress: {processedCount}/{totalRows} ({(processedCount * 100.0 / totalRows):F1}%)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WorkingIdSync] ERROR in progress callback at row {processedCount}: {ex.Message}");
                    }
                }
            }

            // Final progress update
            try
            {
                progressCallback?.Invoke(totalRows, totalRows);
                Console.WriteLine($"[WorkingIdSync] Final progress callback sent: {totalRows}/{totalRows}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorkingIdSync] ERROR in final progress callback: {ex.Message}");
            }

            Console.WriteLine($"[WorkingIdSync] Sync complete:");
            Console.WriteLine($"  - Total: {totalRows}");
            Console.WriteLine($"  - Already Correct: {alreadyCorrect}");
            Console.WriteLine($"  - Histories Added: {workingIdHistoriesAdded}");
            Console.WriteLine($"  - Rider Details Created: {riderDetailsCreated}");
            Console.WriteLine($"  - Duplicates Skipped: {duplicatesSkipped}");
            Console.WriteLine($"  - Name Not Found: {nameNotFound}");
            Console.WriteLine($"  - Errors: {errorRecords}");

            // Add duplicate summary to errors
            if (duplicateSummary.Any())
            {
                errors.Add("=== DUPLICATE WORKING IDS SUMMARY ===");
                foreach (var kvp in duplicateSummary.OrderByDescending(x => x.Value))
                {
                    errors.Add($"WorkingId '{kvp.Key}' appeared {kvp.Value} times (only first processed)");
                }
            }

            var response = new WorkingIdSyncResponse(
                TotalRecordsProcessed: totalRows,
                WorkingIdHistoriesAdded: workingIdHistoriesAdded,
                RiderDetailsCreated: riderDetailsCreated,
                AlreadyCorrect: alreadyCorrect,
                NameNotFound: nameNotFound,
                DuplicatesSkipped: duplicatesSkipped,
                ErrorRecords: errorRecords,
                Details: details,
                ProcessingErrors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WorkingIdSync] FATAL ERROR: {ex}");
            return Result.Failure<WorkingIdSyncResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // Helper method to load employees by Arabic name
    private async Task<Dictionary<string, EmployeeLookupInfo>> LoadEmployeeLookupByName()
    {
        var employees = await _dbcontext.Employees
            .Where(e => !string.IsNullOrEmpty(e.NameAR))
            .Select(e => new EmployeeLookupInfo
            {
                IqamaNo = e.IqamaNo,
                NameAR = e.NameAR,
                NameEN = e.NameEN
            })
            .AsNoTracking()
            .ToListAsync();

        var lookup = new Dictionary<string, EmployeeLookupInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var emp in employees)
        {
            var nameKey = emp.NameAR.Trim().ToLower();
            if (!lookup.ContainsKey(nameKey))
            {
                lookup[nameKey] = emp;
            }
        }

        return lookup;
    }

    // Helper method to load rider details by Iqama
    private async Task<Dictionary<long, RiderDetailsInfo>> LoadRiderDetailsByIqama()
    {
        var riders = await _dbcontext.RiderDetails
            .Include(r => r.Company)
            .Select(r => new RiderDetailsInfo
            {
                IqamaNo = r.EmployeeIqamaNo,
                WorkingId = r.WorkingId,
                CompanyId = r.CompanyId,
                CompanyName = r.Company.Name
            })
            .AsNoTracking()
            .ToListAsync();

        return riders.ToDictionary(r => r.IqamaNo);
    }

    // Internal helper classes
    internal record EmployeeLookupInfo
    {
        public long IqamaNo { get; init; }
        public string NameAR { get; init; } = string.Empty;
        public string NameEN { get; init; } = string.Empty;
    }

    internal record RiderDetailsInfo
    {
        public long IqamaNo { get; init; }
        public string? WorkingId { get; init; }
        public int CompanyId { get; init; }
        public string CompanyName { get; init; } = string.Empty;
    }

    private async Task<Dictionary<string, RiderLookupData>> LoadRiderDetailsLookup()
    {
        var riders = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .Where(r => !string.IsNullOrEmpty(r.WorkingId))
            .Select(r => new RiderLookupData
            {
                WorkingId = r.WorkingId!,
                NameAR = r.Employee.NameAR,
                IqamaNo = r.EmployeeIqamaNo,
                Source = "RiderDetails"
            })
            .AsNoTracking()
            .ToListAsync();

        // Create lookups by both WorkingId and NameAR
        var lookup = new Dictionary<string, RiderLookupData>();

        foreach (var rider in riders)
        {
            var workingIdKey = $"WID:{rider.WorkingId.Trim().ToLower()}";
            var nameKey = $"NAME:{rider.NameAR.Trim().ToLower()}";

            if (!lookup.ContainsKey(workingIdKey))
                lookup[workingIdKey] = rider;

            if (!lookup.ContainsKey(nameKey))
                lookup[nameKey] = rider;
        }

        return lookup;
    }

    private async Task<Dictionary<string, RiderLookupData>> LoadWorkingIdHistoryLookup()
    {
        var history = await _dbcontext.RiderWorkingIdHistories
            .Include(h => h.Employee)
            .Where(h => !string.IsNullOrEmpty(h.WorkingId))
            .Select(h => new RiderLookupData
            {
                WorkingId = h.WorkingId,
                NameAR = h.Employee.NameAR,
                IqamaNo = h.RiderIqamaNo,
                Source = "WorkingIdHistory"
            })
            .AsNoTracking()
            .ToListAsync();

        var lookup = new Dictionary<string, RiderLookupData>();

        foreach (var item in history)
        {
            var workingIdKey = $"WID:{item.WorkingId.Trim().ToLower()}";
            var nameKey = $"NAME:{item.NameAR.Trim().ToLower()}";

            if (!lookup.ContainsKey(workingIdKey))
                lookup[workingIdKey] = item;

            if (!lookup.ContainsKey(nameKey))
                lookup[nameKey] = item;
        }

        return lookup;
    }

    private RiderVerificationResult VerifyRiderData(
        string excelWorkingId,
        string excelNameAR,
        Dictionary<string, RiderLookupData> riderDetailsLookup,
        Dictionary<string, RiderLookupData> workingIdHistoryLookup)
    {
        var workingIdKey = $"WID:{excelWorkingId.Trim().ToLower()}";
        var nameKey = $"NAME:{excelNameAR.Trim().ToLower()}";

        RiderLookupData? foundByWorkingId = null;
        RiderLookupData? foundByName = null;
        string? foundInTable = null;

        // Check RiderDetails first
        if (riderDetailsLookup.TryGetValue(workingIdKey, out var rdByWorkingId))
        {
            foundByWorkingId = rdByWorkingId;
            foundInTable = "RiderDetails";
        }

        if (riderDetailsLookup.TryGetValue(nameKey, out var rdByName))
        {
            foundByName = rdByName;
            if (foundInTable == null)
                foundInTable = "RiderDetails";
        }

        // Check WorkingIdHistory if not found
        if (foundByWorkingId == null && workingIdHistoryLookup.TryGetValue(workingIdKey, out var whByWorkingId))
        {
            foundByWorkingId = whByWorkingId;
            foundInTable = foundInTable == "RiderDetails" ? "Both" : "WorkingIdHistory";
        }

        if (foundByName == null && workingIdHistoryLookup.TryGetValue(nameKey, out var whByName))
        {
            foundByName = whByName;
            if (foundInTable == null)
                foundInTable = "WorkingIdHistory";
            else if (foundInTable == "RiderDetails")
                foundInTable = "Both";
        }

        // Determine status
        if (foundByWorkingId != null && foundByName != null)
        {
            // Check if they match the same person
            if (foundByWorkingId.IqamaNo == foundByName.IqamaNo &&
                foundByWorkingId.WorkingId.Equals(excelWorkingId, StringComparison.OrdinalIgnoreCase) &&
                foundByWorkingId.NameAR.Equals(excelNameAR, StringComparison.OrdinalIgnoreCase))
            {
                return new RiderVerificationResult
                {
                    Status = VerificationStatus.FullyMatched,
                    FoundInTable = foundInTable,
                    ActualWorkingId = foundByWorkingId.WorkingId,
                    ActualNameAR = foundByWorkingId.NameAR,
                    FoundIqamaNo = foundByWorkingId.IqamaNo
                };
            }

            // WorkingId exists but name is different
            if (foundByWorkingId.WorkingId.Equals(excelWorkingId, StringComparison.OrdinalIgnoreCase))
            {
                return new RiderVerificationResult
                {
                    Status = VerificationStatus.WorkingIdFoundNameMismatch,
                    FoundInTable = foundInTable,
                    ActualWorkingId = foundByWorkingId.WorkingId,
                    ActualNameAR = foundByWorkingId.NameAR,
                    FoundIqamaNo = foundByWorkingId.IqamaNo
                };
            }

            // Name exists but WorkingId is different
            return new RiderVerificationResult
            {
                Status = VerificationStatus.NameFoundWorkingIdMismatch,
                FoundInTable = foundInTable,
                ActualWorkingId = foundByName.WorkingId,
                ActualNameAR = foundByName.NameAR,
                FoundIqamaNo = foundByName.IqamaNo
            };
        }

        if (foundByWorkingId != null)
        {
            return new RiderVerificationResult
            {
                Status = VerificationStatus.WorkingIdFoundNameMismatch,
                FoundInTable = foundInTable,
                ActualWorkingId = foundByWorkingId.WorkingId,
                ActualNameAR = foundByWorkingId.NameAR,
                FoundIqamaNo = foundByWorkingId.IqamaNo
            };
        }

        if (foundByName != null)
        {
            return new RiderVerificationResult
            {
                Status = VerificationStatus.NameFoundWorkingIdMismatch,
                FoundInTable = foundInTable,
                ActualWorkingId = foundByName.WorkingId,
                ActualNameAR = foundByName.NameAR,
                FoundIqamaNo = foundByName.IqamaNo
            };
        }

        return new RiderVerificationResult
        {
            Status = VerificationStatus.CompletelyNotFound
        };
    }


    // ===========================
    // Helper Methods
    // ===========================

    private IXLRow? FindRiderVerificationHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
            "riderId", "Working ID", "معرف العمل", "رقم العمل",
            "riderName", "Name AR", "الاسم", "الاسم العربي", "اسم السائق"
        };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private RiderVerificationColumnMapping BuildRiderVerificationColumnMapping(IXLRow headerRow)
    {
        var mapping = new RiderVerificationColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.WorkingIdCol = FindColumn(cells,
            "driverId", "Working ID", "WorkingID", "معرف العمل", "رقم العمل", "المعرف");

        mapping.NameARCol = FindColumn(cells,
            "driverName", "Name AR", "الاسم", "الاسم العربي", "اسم السائق", "اسم الموظف");

        var missing = new List<string>();
        if (mapping.WorkingIdCol == 0) missing.Add("WorkingId");
        if (mapping.NameARCol == 0) missing.Add("NameAR");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private RiderVerificationRowData ParseRiderVerificationRowData(
        IXLRow row,
        RiderVerificationColumnMapping map,
        int rowNumber)
    {
        var data = new RiderVerificationRowData { RowNumber = rowNumber };

        try
        {
            data.WorkingId = GetCellValue(row, map.WorkingIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.WorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "WorkingId is required";
                return data;
            }

            data.NameAR = GetCellValue(row, map.NameARCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.NameAR))
            {
                data.IsValid = false;
                data.ErrorMessage = "Arabic Name is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ===========================
    // Internal Classes
    // ===========================

    // ============================================
    // ADD THESE TO Application/Service/IImportService.cs
    // AT THE END OF THE FILE (after WorkingIdSyncResponse)
    // ============================================

    public record RiderShiftBulkImportResponse(
        int TotalRecordsProcessed,
        int SuccessfulShifts,
        int UpdatedShifts,
        int SkippedDuplicates,
        int WorkingIdNotFound,
        int HousingNotFound,
        int ValidationErrors,
        List<RiderShiftImportDetail> Details,
        List<string> ProcessingErrors,
        DateTime ProcessedAt
    );

    public record RiderShiftImportDetail(
        int RowNumber,
        string WorkingIdFromExcel,
        DateOnly ShiftDate,
        ImportStatus Status,
        string? Action,
        int? FoundRiderId,
        long? FoundIqamaNo,
        string? FoundInTable,
        int? HousingId,
        int AcceptedOrders,
        string ShiftStatus,
        string? ErrorMessage
    );

    public enum ImportStatus
    {
        Success = 1,
        Updated = 2,
        SkippedDuplicate = 3,
        WorkingIdNotFound = 4,
        HousingNotFound = 5,
        ValidationError = 6
    }
    internal class RiderVerificationColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int WorkingIdCol { get; set; }
        public int NameARCol { get; set; }
    }

    internal class RiderVerificationRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? WorkingId { get; set; }
        public string? NameAR { get; set; }
    }

    internal class RiderLookupData
    {
        public string WorkingId { get; set; } = string.Empty;
        public string NameAR { get; set; } = string.Empty;
        public long IqamaNo { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    internal class RiderVerificationResult
    {
        public VerificationStatus Status { get; set; }
        public string? FoundInTable { get; set; }
        public string? ActualWorkingId { get; set; }
        public string? ActualNameAR { get; set; }
        public long? FoundIqamaNo { get; set; }
    }
    public async Task<Result<DeletedEmployeeImportResponse>> ImportDeletedEmployeesAsync(
    IFormFile file,
    string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<DeletedEmployeeImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<DeletedEmployeeImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<DeletedEmployeeImportRowResult>();
        var errors = new List<string>();
        int successfulImports = 0;
        int failedRecords = 0;
        int duplicateIqamas = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<DeletedEmployeeImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindDeletedEmployeeHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<DeletedEmployeeImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildDeletedEmployeeColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<DeletedEmployeeImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            // Load company lookup dictionary
            var companies = await _dbcontext.Companies
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Name.Trim().ToLower(), c => c.Id);

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseDeletedEmployeeRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new DeletedEmployeeImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            rowData.NameEN,
                            rowData.NameAR,
                            rowData.WorkingId,
                            rowData.CompanyName,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Check if already exists in DeletedEmployees
                    var existingDeleted = await _dbcontext.DeletedEmployees
                        .AnyAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (existingDeleted)
                    {
                        duplicateIqamas++;
                        failedRecords++;
                        results.Add(new DeletedEmployeeImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NameEN,
                            rowData.NameAR,
                            rowData.WorkingId,
                            rowData.CompanyName,
                            warnings,
                            "Deleted employee with this Iqama already exists"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Resolve CompanyId if CompanyName provided
                    int? companyId = null;
                    if (!string.IsNullOrWhiteSpace(rowData.CompanyName))
                    {
                        if (companies.TryGetValue(rowData.CompanyName.Trim().ToLower(), out int cId))
                        {
                            companyId = cId;
                        }
                        else
                        {
                            warnings.Add($"Company '{rowData.CompanyName}' not found - CompanyId set to null");
                        }
                    }

                    // Create DeletedEmployee record
                    var deletedEmployee = new DeletedEmployees
                    {
                        IqamaNo = rowData.IqamaNo!.Value,
                        NameEN = rowData.NameEN ?? "Unknown",
                        NameAR = rowData.NameAR ?? "غير معروف",
                        IqamaEndM = rowData.IqamaEndM ?? DateOnly.FromDateTime(DateTime.Now.AddYears(1)),
                        IqamaEndH = rowData.IqamaEndH ?? DateOnly.FromDateTime(DateTime.Now.AddYears(1)),
                        PassportNo = rowData.PassportNo,
                        PassportEnd = rowData.PassportEnd,
                        Sponsor = rowData.Sponsor ?? "الخدمة السريعة",
                        JobTitle = rowData.JobTitle ?? "سائق دراجة نارية",
                        Country = rowData.Country ?? "Unknown",
                        Phone = rowData.Phone ?? "05",
                        DateOfBirth = rowData.DateOfBirth ?? DateTime.Parse("1990-01-01"),
                        Status = rowData.Status ?? "disable",
                        AcountStatus = rowData.AcountStatus ?? "قيد التشغيل",
                        IBAN = rowData.IBAN,
                        INKSA = rowData.INKSA,
                        WorkingId = rowData.WorkingId ?? "N/A",
                        TshirtSize = rowData.TshirtSize,
                        LicenseNumber = rowData.LicenseNumber,
                        CompanyId = companyId,
                        HousingId = null,
                        VehicleId = null,
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    };

                    await _dbcontext.DeletedEmployees.AddAsync(deletedEmployee);
                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulImports++;
                    results.Add(new DeletedEmployeeImportRowResult(
                        rowNumber,
                        true,
                        deletedEmployee.IqamaNo.ToString(),
                        deletedEmployee.NameEN,
                        deletedEmployee.NameAR,
                        deletedEmployee.WorkingId,
                        rowData.CompanyName,
                        warnings,
                        null
                    ));

                    if (string.IsNullOrWhiteSpace(rowData.WorkingId))
                    {
                        warnings.Add("WorkingId not provided - using default 'N/A'");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new DeletedEmployeeImportRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        null,
                        null,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new DeletedEmployeeImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulImports: successfulImports,
                FailedRecords: failedRecords,
                DuplicateIqamas: duplicateIqamas,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<DeletedEmployeeImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingAssignmentResponse>> BulkAssignEmployeesToHousingAsync(
     IFormFile file,
     string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<HousingAssignmentResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<HousingAssignmentResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<HousingAssignmentRowResult>();
        var errors = new List<string>();
        int successfulAssignments = 0;
        int failedRecords = 0;
        int employeeNotFound = 0;
        int housingNotFound = 0;
        int alreadyAssigned = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<HousingAssignmentResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindHousingHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<HousingAssignmentResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildHousingColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<HousingAssignmentResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            // Load housing lookup dictionary
            var housings = await _dbcontext.Housings
                .AsNoTracking()
                .ToDictionaryAsync(h => h.Name.Trim().ToLower(), h => h.Id);

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseHousingRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new HousingAssignmentRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            "N/A",
                            "N/A",
                            rowData.HousingName ?? "N/A",
                            false,
                            null,
                            false,
                            null,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Find employee with rider details
                    var employee = await _dbcontext.Employees
                        .Include(e => e.Housing)
                        .Include(e => e.RiderDetails)
                            .ThenInclude(rd => rd.Company)
                        .FirstOrDefaultAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (employee == null)
                    {
                        employeeNotFound++;
                        failedRecords++;
                        results.Add(new HousingAssignmentRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            "N/A",
                            "N/A",
                            rowData.HousingName!,
                            false,
                            null,
                            false,
                            null,
                            warnings,
                            "Employee with this Iqama number not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Check if this person is a rider
                    bool isRider = employee.RiderDetails != null;
                    string? companyName = employee.RiderDetails?.Company?.Name;

                    if (isRider)
                    {
                        warnings.Add($"This is a rider from company: {companyName}");
                    }

                    // Find housing
                    if (!housings.TryGetValue(rowData.HousingName!.Trim().ToLower(), out int housingId))
                    {
                        housingNotFound++;
                        failedRecords++;
                        results.Add(new HousingAssignmentRowResult(
                            rowNumber,
                            false,
                            employee.IqamaNo.ToString(),
                            employee.NameEN,
                            employee.NameAR,
                            rowData.HousingName!,
                            isRider,
                            companyName,
                            false,
                            null,
                            warnings,
                            $"Housing '{rowData.HousingName}' not found in database"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Check if already assigned
                    string? previousHousing = null;
                    bool wasAlreadyAssigned = false;

                    if (employee.HousingId.HasValue)
                    {
                        previousHousing = employee.Housing?.Name;

                        if (employee.HousingId == housingId)
                        {
                            alreadyAssigned++;
                            warnings.Add($"Already assigned to {rowData.HousingName}");

                            results.Add(new HousingAssignmentRowResult(
                                rowNumber,
                                true,
                                employee.IqamaNo.ToString(),
                                employee.NameEN,
                                employee.NameAR,
                                rowData.HousingName!,
                                isRider,
                                companyName,
                                true,
                                previousHousing,
                                warnings,
                                null
                            ));

                            await transaction.CommitAsync();
                            continue;
                        }

                        wasAlreadyAssigned = true;
                        warnings.Add($"Changed housing from '{previousHousing}' to '{rowData.HousingName}'");
                    }

                    // Assign to housing (works for both employees and riders)
                    employee.HousingId = housingId;

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulAssignments++;
                    results.Add(new HousingAssignmentRowResult(
                        rowNumber,
                        true,
                        employee.IqamaNo.ToString(),
                        employee.NameEN,
                        employee.NameAR,
                        rowData.HousingName!,
                        isRider,
                        companyName,
                        wasAlreadyAssigned,
                        previousHousing,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new HousingAssignmentRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        "N/A",
                        "N/A",
                        "N/A",
                        false,
                        null,
                        false,
                        null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new HousingAssignmentResponse(
                TotalRecords: dataRows.Count,
                SuccessfulAssignments: successfulAssignments,
                FailedRecords: failedRecords,
                EmployeeNotFound: employeeNotFound,
                HousingNotFound: housingNotFound,
                AlreadyAssigned: alreadyAssigned,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingAssignmentResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }


    public async Task<Result<DirectImportResponse>> ImportEmployeesAndRidersAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<DirectImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<DirectImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<ImportRowResult>();
        var errors = new List<string>();
        int successfulEmployees = 0;
        int successfulRiders = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<DirectImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<DirectImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            // Map columns by finding their positions
            var columnMap = BuildColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<DirectImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            // Load company lookup dictionary
            var companies = await _dbcontext.Companies
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Name.Trim().ToLower(), c => c.Id);

            var dataRows = worksheet.RowsUsed()
            .Where(r => r.RowNumber() > headerRow.RowNumber())
            .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new ImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo.ToString() ?? "N/A",
                            rowData.NameEN ?? "N/A",
                            rowData.NameAR ?? "N/A",
                            rowData.CompanyName,
                            false, false, false, false,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Process Employee
                    var (employeeCreated, employeeUpdated, employeeError) =
                        await ProcessEmployee(rowData, warnings);

                    if (employeeError != null)
                    {
                        await transaction.RollbackAsync();
                        failedRecords++;
                        results.Add(new ImportRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo.ToString(),
                            rowData.NameEN ?? "N/A",
                            rowData.NameAR ?? "N/A",
                            rowData.CompanyName,
                            false, false, false, false,
                            warnings,
                            employeeError
                        ));
                        continue;
                    }

                    if (employeeCreated || employeeUpdated)
                        successfulEmployees++;

                    // Process Rider (if company data exists)
                    bool riderCreated = false;
                    bool riderUpdated = false;

                    if (!string.IsNullOrWhiteSpace(rowData.CompanyName))
                    {
                        if (companies.TryGetValue(rowData.CompanyName.Trim().ToLower(), out int companyId))
                        {
                            rowData.CompanyId = companyId;
                            var (created, updated, riderError) =
                                await ProcessRider(rowData, warnings);

                            if (riderError != null)
                            {
                                warnings.Add($"Rider processing failed: {riderError}");
                            }
                            else
                            {
                                riderCreated = created;
                                riderUpdated = updated;
                                if (created || updated)
                                    successfulRiders++;
                            }
                        }
                        else
                        {
                            warnings.Add($"Company '{rowData.CompanyName}' not found in database");
                        }
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    results.Add(new ImportRowResult(
                        rowNumber,
                        true,
                        rowData.IqamaNo.ToString(),
                        rowData.NameEN ?? "",
                        rowData.NameAR ?? "",
                        rowData.CompanyName,
                        employeeCreated,
                        employeeUpdated,
                        riderCreated,
                        riderUpdated,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new ImportRowResult(
                        rowNumber,
                        false,
                        "N/A", "N/A", "N/A", null,
                        false, false, false, false,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new DirectImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulEmployees: successfulEmployees,
                SuccessfulRiders: successfulRiders,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<DirectImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    public async Task<Result<VehicleImportResponse>> ImportVehiclesAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleImportRowResult>();
        var errors = new List<string>();
        int successfulVehicles = 0;
        int updatedVehicles = 0;
        int assignedToRiders = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<VehicleImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindHeaderRow1(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<VehicleImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildColumnMapping1(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<VehicleImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new VehicleImportRowResult(
                            rowNumber,
                            false,
                            rowData.VehicleNumber ?? "N/A",
                            rowData.PlateNumberA ?? "N/A",
                            rowData.SerialNumber,
                            false, false, false, null,
                            new List<string>(),
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    var warnings = new List<string>();
                    var changes = new List<string>();

                    var (vehicleCreated, vehicleUpdated, vehicleError, vehicleChanges) =
                        await ProcessVehicle(rowData, warnings, uploadedBy);

                    if (vehicleError != null)
                    {
                        await transaction.RollbackAsync();
                        failedRecords++;
                        results.Add(new VehicleImportRowResult(
                            rowNumber,
                            false,
                            rowData.VehicleNumber!,
                            rowData.PlateNumberA!,
                            rowData.SerialNumber,
                            false, false, false, null,
                            new List<string>(),
                            warnings,
                            vehicleError
                        ));
                        continue;
                    }

                    if (vehicleCreated)
                        successfulVehicles++;
                    else if (vehicleUpdated)
                        updatedVehicles++;

                    changes.AddRange(vehicleChanges);

                    bool assignedToRider = false;
                    string? assignedRiderIqama = null;

                    if (rowData.RiderIqamaNo.HasValue)
                    {
                        var (assigned, assignError) =
                            await ProcessRiderAssignment(rowData, warnings, uploadedBy);

                        if (assignError != null)
                        {
                            warnings.Add($"Rider assignment failed: {assignError}");
                        }
                        else if (assigned)
                        {
                            assignedToRider = true;
                            assignedRiderIqama = rowData.RiderIqamaNo.Value.ToString();
                            assignedToRiders++;
                            changes.Add($"Assigned to rider {rowData.RiderIqamaNo.Value}");
                        }
                    }

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    results.Add(new VehicleImportRowResult(
                        rowNumber,
                        true,
                        rowData.VehicleNumber!,
                        rowData.PlateNumberA!,
                        rowData.SerialNumber,
                        vehicleCreated,
                        vehicleUpdated,
                        assignedToRider,
                        assignedRiderIqama,
                        changes,
                        warnings,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new VehicleImportRowResult(
                        rowNumber,
                        false,
                        "N/A", "N/A", 0,
                        false, false, false, null,
                        new List<string>(),
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new VehicleImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulVehicles: successfulVehicles,
                UpdatedVehicles: updatedVehicles,
                AssignedToRiders: assignedToRiders,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private VehicleColumnMapping BuildColumnMapping1(IXLRow headerRow)
    {
        var mapping = new VehicleColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.VehicleNumberCol = FindColumn1(cells,
            "VehicleNumber", "Vehicle Number", "رقم الهيكل", "Vehicle ID", "VIN");

        mapping.SerialNumberCol = FindColumn1(cells,
            "SerialNumber", "Serial Number", "الرقم التسلسلي", "Serial No", "Serial");

        mapping.PlateNumberACol = FindColumn1(cells,
            "PlateNumberA", "Plate Number A", "رقم اللوحة", "اللوحة العربية", "Plate A", "Arabic Plate");

        mapping.PlateNumberECol = FindColumn1(cells,
            "PlateNumberE", "Plate Number E", "رقم اللوحة En", "اللوحة الانجليزية", "Plate E", "English Plate");

        mapping.VehicleTypeCol = FindColumn1(cells,
            "VehicleType", "Vehicle Type", "نوع المركبة", "طراز المركبة");

        mapping.ManufacturerCol = FindColumn1(cells,
            "Manufacturer", "الصانع", "المصنع", "ماركة المركبة", "Brand");

        mapping.ManufactureYearCol = FindColumn1(cells,
            "ManufactureYear", "Manufacture Year", "سنة الصنع", "Year", "Model Year");

        mapping.LicenseExpiryDateCol = FindColumn1(cells,
            "LicenseExpiryDate", "License Expiry Date", "تاريخ انتهاء الرخصة", "License Expiry", "Expiry Date");

        mapping.LocationCol = FindColumn1(cells,
            "Location", "الموقع", "المكان");

        mapping.StatusCol = FindColumn1(cells,
            "Status", "الحالة", "ملاحظات");

        mapping.RiderIqamaNoCol = FindColumn1(cells,
            "RiderIqamaNo", "Rider Iqama", "رقم اقامة السائق", "Driver Iqama", "EmployeeIqamaNo", "IqamaNo");

        var missing = new List<string>();
        if (mapping.VehicleNumberCol == 0) missing.Add("Vehicle Number");
        if (mapping.SerialNumberCol == 0) missing.Add("Serial Number");
        if (mapping.PlateNumberACol == 0) missing.Add("Plate Number A");
        if (mapping.PlateNumberECol == 0) missing.Add("Plate Number E");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private int FindColumn1(List<IXLCell> cells, params string[] possibleNames)
    {
        foreach (var cell in cells)
        {
            try
            {
                if (cell.IsEmpty()) continue;

                string headerValue = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString().Trim()
                    : cell.GetString().Trim();

                if (string.IsNullOrWhiteSpace(headerValue)) continue;

                foreach (var name in possibleNames)
                {
                    if (headerValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }

                string headerNoSpaces = headerValue.Replace(" ", "");
                foreach (var name in possibleNames)
                {
                    string nameNoSpaces = name.Replace(" ", "");
                    if (headerNoSpaces.Equals(nameNoSpaces, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }

                foreach (var name in possibleNames)
                {
                    if (headerValue.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }
            }
            catch { }
        }

        return 0;
    }

    private VehicleRowData ParseRowData(IXLRow row, VehicleColumnMapping map, int rowNumber)
    {
        var data = new VehicleRowData { RowNumber = rowNumber };

        try
        {
            data.VehicleNumber = GetCellValue1(row, map.VehicleNumberCol);
            if (string.IsNullOrWhiteSpace(data.VehicleNumber))
            {
                data.IsValid = false;
                data.ErrorMessage = "Vehicle Number is required";
                return data;
            }

            var serialStr = GetCellValue1(row, map.SerialNumberCol);
            if (string.IsNullOrWhiteSpace(serialStr) ||
                !int.TryParse(serialStr.Replace(" ", ""), out int serialNumber))
            {
                data.IsValid = false;
                data.ErrorMessage = "Valid Serial Number is required";
                return data;
            }
            data.SerialNumber = serialNumber;

            data.PlateNumberA = GetCellValue1(row, map.PlateNumberACol)?.Replace(" ", "");
            if (string.IsNullOrWhiteSpace(data.PlateNumberA))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number A is required";
                return data;
            }

            data.PlateNumberE = GetCellValue1(row, map.PlateNumberECol)?.Replace(" ", "");
            if (string.IsNullOrWhiteSpace(data.PlateNumberE))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number E is required";
                return data;
            }

            data.VehicleType = GetCellValue1(row, map.VehicleTypeCol) ?? "دراجة نارية";
            data.Manufacturer = GetCellValue1(row, map.ManufacturerCol) ?? "Unknown";
            data.Location = GetCellValue1(row, map.LocationCol) ?? "الشركة";
            data.Status = GetCellValue1(row, map.StatusCol) ?? "Returned";

            var yearStr = GetCellValue1(row, map.ManufactureYearCol);
            data.ManufactureYear = int.TryParse(yearStr, out int year) && year >= 1900 && year <= DateTime.Now.Year + 1
                ? year
                : DateTime.Now.Year;

            data.LicenseExpiryDate = ParseDate(GetCellValue1(row, map.LicenseExpiryDateCol))
                ?? DateOnly.FromDateTime(DateTime.Now.AddYears(1));

            var riderIqamaStr = GetCellValue1(row, map.RiderIqamaNoCol);
            if (!string.IsNullOrWhiteSpace(riderIqamaStr) &&
                long.TryParse(riderIqamaStr.Replace(" ", ""), out long riderIqama))
            {
                data.RiderIqamaNo = riderIqama;
            }

            data.OwnerName = "الخدمة السريعة";
            data.OwnerId = 7010962889;

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    private async Task<(bool created, bool updated, string? error, List<string> changes)> ProcessVehicle(
        VehicleRowData data,
        List<string> warnings,
        string uploadedBy)
    {
        var changes = new List<string>();

        try
        {
            var conflictingVehicle = await _dbcontext.Vehicles
                .Where(v => v.VehicleNumber != data.VehicleNumber &&
                           (v.SerialNumber == data.SerialNumber ||
                            v.PlateNumberA == data.PlateNumberA ||
                            v.PlateNumberE == data.PlateNumberE))
                .FirstOrDefaultAsync();

            if (conflictingVehicle != null)
            {
                return (false, false,
                    $"Conflict: Serial/Plate already exists on vehicle {conflictingVehicle.VehicleNumber}",
                    changes);
            }

            var vehicle = await _dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == data.VehicleNumber);

            if (vehicle == null)
            {
                vehicle = new Vehicle
                {
                    VehicleNumber = data.VehicleNumber!,
                    SerialNumber = data.SerialNumber,
                    PlateNumberA = data.PlateNumberA!,
                    PlateNumberE = data.PlateNumberE!,
                    VehicleType = data.VehicleType!,
                    Manufacturer = data.Manufacturer!,
                    ManufactureYear = data.ManufactureYear,
                    LicenseExpiryDate = data.LicenseExpiryDate!.Value,
                    Location = data.Location!,
                    OwnerName = data.OwnerName!,
                    OwnerId = data.OwnerId,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                };

                await _dbcontext.Vehicles.AddAsync(vehicle);
                changes.Add("Vehicle created");

                return (true, false, null, changes);
            }
            else
            {
                bool hasChanges = false;

                if (vehicle.SerialNumber != data.SerialNumber)
                {
                    changes.Add($"Serial changed: {vehicle.SerialNumber} → {data.SerialNumber}");
                    vehicle.SerialNumber = data.SerialNumber;
                    hasChanges = true;
                }

                if (vehicle.PlateNumberA != data.PlateNumberA)
                {
                    changes.Add($"Plate A changed: {vehicle.PlateNumberA} → {data.PlateNumberA}");
                    vehicle.PlateNumberA = data.PlateNumberA!;
                    hasChanges = true;
                }

                if (vehicle.PlateNumberE != data.PlateNumberE)
                {
                    changes.Add($"Plate E changed: {vehicle.PlateNumberE} → {data.PlateNumberE}");
                    vehicle.PlateNumberE = data.PlateNumberE!;
                    hasChanges = true;
                }

                if (vehicle.VehicleType != data.VehicleType)
                {
                    vehicle.VehicleType = data.VehicleType!;
                    hasChanges = true;
                }

                if (vehicle.Manufacturer != data.Manufacturer)
                {
                    vehicle.Manufacturer = data.Manufacturer!;
                    hasChanges = true;
                }

                if (vehicle.ManufactureYear != data.ManufactureYear)
                {
                    vehicle.ManufactureYear = data.ManufactureYear;
                    hasChanges = true;
                }

                if (vehicle.LicenseExpiryDate != data.LicenseExpiryDate!.Value)
                {
                    vehicle.LicenseExpiryDate = data.LicenseExpiryDate.Value;
                    hasChanges = true;
                }

                if (vehicle.Location != data.Location)
                {
                    changes.Add($"Location changed: {vehicle.Location} → {data.Location}");
                    vehicle.Location = data.Location!;
                    hasChanges = true;
                }

                await HandleStatusChanges(vehicle, data, changes, uploadedBy);

                if (hasChanges)
                {
                    changes.Add("Vehicle updated");
                    return (false, true, null, changes);
                }
                else
                {
                    warnings.Add("Vehicle exists with same data - no changes");
                    return (false, false, null, changes);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, false, $"Vehicle processing error: {ex.Message}", changes);
        }
    }

    private async Task HandleStatusChanges(
        Vehicle vehicle,
        VehicleRowData data,
        List<string> changes,
        string uploadedBy)
    {
        // Check current status
        var currentActiveStatus = await _dbcontext.RiderVehicleStatus
            .Where(s => s.VehicleNumber == vehicle.VehicleNumber && s.IsActive)
            .FirstOrDefaultAsync();

        string currentStatus = currentActiveStatus?.StatusType.ToString() ?? "Available";

        // If status in Excel differs from current status
        if (data.Status != null && !data.Status.Equals(currentStatus, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"Status changed: {currentStatus} → {data.Status}");

            // Deactivate old status
            if (currentActiveStatus != null)
            {
                currentActiveStatus.IsActive = false;
            }

            // Add new status if not "Available"
            if (!data.Status.Equals("Available", StringComparison.OrdinalIgnoreCase))
            {
                VehicleStatusType newStatusType = data.Status.ToLower() switch
                {
                    "problem" => VehicleStatusType.Problem,
                    "stolen" => VehicleStatusType.Stolen,
                    "breakup" or "break up" => VehicleStatusType.BreakUp,
                    _ => VehicleStatusType.Returned
                };

                _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                {
                    VehicleNumber = vehicle.VehicleNumber,
                    EmployeeIqamaNo = null,
                    StatusType = newStatusType,
                    Reason = $"Status updated via import by {uploadedBy}",
                    IsActive = true,
                    Timestamp = DateTime.UtcNow.AddHours(3)
                });
            }
        }
    }

    private async Task<(bool assigned, string? error)> ProcessRiderAssignment(
        VehicleRowData data,
        List<string> warnings,
        string uploadedBy)
    {
        try
        {
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == data.RiderIqamaNo!.Value);

            if (rider == null)
                return (false, $"Rider with Iqama {data.RiderIqamaNo} not found");

            if (rider.Employee.Status != "enable")
                return (false, "Rider is disabled");

            // Check if rider already has a vehicle
            if (!string.IsNullOrEmpty(rider.VehicleNumber))
            {
                warnings.Add($"Rider already has vehicle {rider.VehicleNumber}, replacing it");

                // Return old vehicle
                var oldVehicleStatus = await _dbcontext.RiderVehicleStatus
                    .FirstOrDefaultAsync(s => s.VehicleNumber == rider.VehicleNumber &&
                                             s.EmployeeIqamaNo == rider.EmployeeIqamaNo &&
                                             s.IsActive &&
                                             s.StatusType == VehicleStatusType.Taken);

                if (oldVehicleStatus != null)
                {
                    oldVehicleStatus.IsActive = false;
                    _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                    {
                        VehicleNumber = rider.VehicleNumber,
                        EmployeeIqamaNo = rider.EmployeeIqamaNo,
                        StatusType = VehicleStatusType.Returned,
                        Reason = "Replaced by import",
                        IsActive = false,
                        Timestamp = DateTime.UtcNow.AddHours(3)
                    });
                }
            }

            // Check if vehicle is available
            var vehicleUnavailable = await _dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == data.VehicleNumber &&
                              s.IsActive &&
                              (s.StatusType == VehicleStatusType.Taken ||
                               s.StatusType == VehicleStatusType.Problem ||
                               s.StatusType == VehicleStatusType.Stolen));

            if (vehicleUnavailable)
            {
                // Deactivate old statuses
                var oldStatuses = await _dbcontext.RiderVehicleStatus
                    .Where(s => s.VehicleNumber == data.VehicleNumber && s.IsActive)
                    .ToListAsync();

                foreach (var status in oldStatuses)
                {
                    status.IsActive = false;
                }

                warnings.Add("Vehicle was unavailable, forcing assignment");
            }

            // Assign vehicle to rider
            rider.VehicleNumber = data.VehicleNumber;

            // Add history
            _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                VehicleNumber = data.VehicleNumber!,
                EmployeeIqamaNo = data.RiderIqamaNo!.Value,
                StatusType = VehicleStatusType.Taken,
                Reason = $"Assigned via import by {uploadedBy}",
                IsActive = true,
                Timestamp = DateTime.UtcNow.AddHours(3)
            });

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Assignment error: {ex.Message}");
        }
    }

    private IXLRow FindHeaderRow1(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
            "VehicleNumber", "Vehicle Number", "SerialNumber", "Serial Number",
            "PlateNumberA", "Plate A", "PlateNumberE", "Plate E",
            "رقم المركبة", "الرقم التسلسلي", "رقم اللوحة"
        };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        // Fallback
        return worksheet.Row(1);
    }

    private string? GetCellValue1(IXLRow row, int columnIndex)
    {
        if (columnIndex == 0) return null;

        try
        {
            var cell = row.Cell(columnIndex);
            if (cell.IsEmpty()) return null;

            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime().ToString("dd/MM/yyyy");

            if (cell.DataType == XLDataType.Number)
                return cell.GetDouble().ToString();

            if (cell.DataType == XLDataType.Text)
                return cell.GetText().Trim();

            if (cell.DataType == XLDataType.Boolean)
                return cell.GetBoolean().ToString();

            return cell.Value.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private DateOnly? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        string[] formats = {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
            "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d",
            "dd.MM.yyyy", "d.M.yyyy"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return DateOnly.FromDateTime(date);
            }
        }

        if (DateTime.TryParse(dateStr, out DateTime generalDate))
        {
            return DateOnly.FromDateTime(generalDate);
        }

        return null;
    }

    private ColumnMapping BuildColumnMapping(IXLRow headerRow)
    {
        var mapping = new ColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();

                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
                Console.WriteLine($"Header Column {cell.Address.ColumnNumber} ({cell.Address.ColumnLetter}): '{val}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading column {cell.Address.ColumnNumber}: {ex.Message}");
            }
        }

        // Map columns
        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة", "Iqama No", "IqamaNo", "الاقامة");
        Console.WriteLine($"IqamaNo mapped to column: {mapping.IqamaNoCol}");

        mapping.NameARCol = FindColumn(cells,
            "NameAR", "Name AR", "الاسم بالعربية", "الاسم العربي", "Arabic Name", "الاسم");
        Console.WriteLine($"NameAR mapped to column: {mapping.NameARCol}");

        mapping.NameENCol = FindColumn(cells,
            "NameEN", "Name EN", "الاسم بالإنجليزية", "الاسم الانجليزي", "English Name", "Name");
        Console.WriteLine($"NameEN mapped to column: {mapping.NameENCol}");

        mapping.IqamaEndMCol = FindColumn(cells,
            "IqamaEndM", "Iqama End M", "تاريخ انتهاء الاقامة ميلادي", "انتهاء الاقامة", "Iqama Expiry");

        mapping.IqamaEndHCol = FindColumn(cells,
            "IqamaEndH", "Iqama End H", "تاريخ انتهاء الاقامة هجري", "Hijri Date");

        mapping.PassportNoCol = FindColumn(cells,
            "IqamaNumber", "Passport Number", "رقم الجواز", "رقم جواز السفر", "Passport No", "PassportNo");

        mapping.PassportEndCol = FindColumn(cells,
            "PassportEnd", "Passport End", "تاريخ انتهاء الجواز", "انتهاء الجواز", "Passport Expiry");

        mapping.SponsorCol = FindColumn(cells,
            "Sponsor", "الكفيل", "اسم الكفيل", "Sponsor Name");

        mapping.SponsorNoCol = FindColumn(cells,
            "SponsorNo", "Sponsor No", "رقم الكفيل", "Sponsor Number");

        mapping.JobTitleCol = FindColumn(cells,
            "JobTitle", "Job Title", "المسمى الوظيفي", "الوظيفة", "Position");

        mapping.CountryCol = FindColumn(cells,
            "Country", "الجنسية", "البلد", "Nationality");

        mapping.PhoneCol = FindColumn(cells,
            "Phone", "رقم الجوال", "الجوال", "Mobile", "Phone Number");

        mapping.DateOfBirthCol = FindColumn(cells,
            "DateOfBirth", "Date Of Birth", "تاريخ الميلاد", "الميلاد", "Birth Date", "DOB");

        mapping.StatusCol = FindColumn(cells,
            "Status", "الحالة", "Employee Status");

        mapping.IBANCol = FindColumn(cells,
            "IBAN", "رقم الآيبان", "الآيبان", "Bank Account");

        mapping.INKSACol = FindColumn(cells,
            "INKSA", "في السعودية", "In KSA");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingID", "Working ID", "معرف العمل", "رقم العمل", "Work ID", "Employee ID");

        mapping.TshirtSizeCol = FindColumn(cells,
            "TshirtSize", "Tshirt Size", "مقاس القميص", "القميص", "T-shirt", "Shirt Size");

        mapping.LicenseNumberCol = FindColumn(cells,
            "LicenseNumber", "License Number", "رقم الرخصة", "الرخصة", "License No", "Driving License");

        mapping.CompanyNameCol = FindColumn(cells,
            "CompanyName", "Company Name", "اسم الشركة", "الشركة", "Company", "اسم شركة العميل");

        // Validate required columns
        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.NameARCol == 0) missing.Add("NameAR");
        if (mapping.NameENCol == 0) missing.Add("NameEN");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)} \n" +
                                  $"Header row number: {headerRow.RowNumber()}\n" +
                                  $"Columns found in header row:\n{string.Join("\n", actualHeaders)} \n" +
                                  $"Expected variations for NameAR: NameAR, Name AR, الاسم بالعربية  \n" +
                                  $"Expected variations for NameEN: NameEN, Name EN, Name, الاسم بالإنجليزية";

            Console.WriteLine($"ERROR: {mapping.ErrorMessage}");
        }
        else
        {
            mapping.IsValid = true;
            Console.WriteLine("SUCCESS: All required columns found!");
        }

        return mapping;
    }
    private int FindColumn(List<IXLCell> cells, params string[] possibleNames)
    {
        foreach (var cell in cells)
        {
            try
            {
                if (cell.IsEmpty()) continue;

                string headerValue = "";

                if (cell.IsMerged())
                {
                    headerValue = cell.MergedRange().FirstCell().GetString().Trim();
                }
                else
                {
                    switch (cell.DataType)
                    {
                        case XLDataType.Text:
                            headerValue = cell.GetText().Trim();
                            break;
                        case XLDataType.Number:
                            headerValue = cell.GetDouble().ToString().Trim();
                            break;
                        case XLDataType.Boolean:
                            headerValue = cell.GetBoolean().ToString().Trim();
                            break;
                        default:
                            headerValue = cell.GetString().Trim();
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(headerValue))
                    continue;

                // Clean up the header value
                headerValue = headerValue.Trim();

                // Method 1: Exact match (case-insensitive)
                foreach (var name in possibleNames)
                {
                    if (headerValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return cell.Address.ColumnNumber;
                    }
                }

                // Method 2: Match without any spaces (NameAR = Name AR)
                string headerNoSpaces = headerValue.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");
                foreach (var name in possibleNames)
                {
                    string nameNoSpaces = name.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");
                    if (headerNoSpaces.Equals(nameNoSpaces, StringComparison.OrdinalIgnoreCase))
                    {
                        return cell.Address.ColumnNumber;
                    }
                }

                // Method 3: Partial match (contains) - as last resort
                foreach (var name in possibleNames)
                {
                    if (headerValue.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return cell.Address.ColumnNumber;
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        return 0;
    }

    private RowData ParseRowData(IXLRow row, ColumnMapping map, int rowNumber)
    {
        var data = new RowData { RowNumber = rowNumber };
        var errors = new List<string>();

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            // Parse Names (REQUIRED)
            data.NameAR = GetCellValue(row, map.NameARCol);
            data.NameEN = GetCellValue(row, map.NameENCol);

            if (string.IsNullOrWhiteSpace(data.NameAR))
            {
                data.IsValid = false;
                data.ErrorMessage = "Name AR is required";
                return data;
            }

            if (string.IsNullOrWhiteSpace(data.NameEN))
            {
                data.IsValid = false;
                data.ErrorMessage = "Name EN is required";
                return data;
            }

            // Parse Dates
            data.IqamaEndM = ParseGregorianDate(GetCellValue(row, map.IqamaEndMCol));
            data.IqamaEndH = ParseHijriDate(GetCellValue(row, map.IqamaEndHCol));
            data.PassportEnd = ParseGregorianDate(GetCellValue(row, map.PassportEndCol));
            data.DateOfBirth = ParseGregorianDate(GetCellValue(row, map.DateOfBirthCol));

            // Default dates if missing
            data.IqamaEndM ??= DateOnly.FromDateTime(DateTime.Now.AddYears(1));
            data.IqamaEndH ??= DateOnly.FromDateTime(DateTime.Now.AddYears(1));
            data.DateOfBirth ??= DateOnly.FromDateTime(new DateTime(1990, 1, 1));

            // Parse other fields
            data.PassportNo = GetCellValue(row, map.PassportNoCol);
            data.Sponsor = GetCellValue(row, map.SponsorCol) ?? "الخدمة السريعة";

            var sponsorNoStr = GetCellValue(row, map.SponsorNoCol);
            data.SponsorNo = long.TryParse(sponsorNoStr?.Replace(" ", ""), out long sNo) ? sNo : 0;

            data.JobTitle = GetCellValue(row, map.JobTitleCol) ?? "سائق دراجة نارية";
            data.Country = GetCellValue(row, map.CountryCol) ?? "Unknown";
            data.Phone = GetCellValue(row, map.PhoneCol) ?? "05";
            data.Status = GetCellValue(row, map.StatusCol) ?? "enable";
            data.IBAN = GetCellValue(row, map.IBANCol);

            // Parse INKSA
            var inksaStr = GetCellValue(row, map.INKSACol);
            data.INKSA = string.IsNullOrWhiteSpace(inksaStr) ||
                         inksaStr.ToLower() == "yes" ||
                         inksaStr == "1" ||
                         inksaStr.ToLower() == "true";

            // Rider fields
            data.WorkingId = GetCellValue(row, map.WorkingIdCol) ?? "0";
            data.TshirtSize = GetCellValue(row, map.TshirtSizeCol) ?? "s";
            data.LicenseNumber = GetCellValue(row, map.LicenseNumberCol) ?? "0";
            data.CompanyName = GetCellValue(row, map.CompanyNameCol);

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    private IXLRow FindHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "NameEN", "Name EN", "NameAR", "Name AR",
        "IqamaNumber", "Iqama Number", "رقم الإقامة", "رقم الاقامة",
        "Phone", "Sponsor", "Country"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = "";
                    if (cell.IsMerged())
                    {
                        value = cell.MergedRange().FirstCell().GetString().Trim();
                    }
                    else
                    {
                        value = cell.GetString().Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        cellValues.Add(value);
                    }
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 3)
            {
                Console.WriteLine($"Found header row at row {i} with {matchCount} matching columns");
                return row;
            }
        }

        IXLRow? bestRow = null;
        int maxNonEmptyCells = 0;

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var nonEmptyCells = row.CellsUsed().Count(c =>
                !string.IsNullOrWhiteSpace(GetCellValueSafe(c)));

            if (nonEmptyCells > maxNonEmptyCells)
            {
                maxNonEmptyCells = nonEmptyCells;
                bestRow = row;
            }
        }

        Console.WriteLine($"Fallback: Using row {bestRow?.RowNumber()} with {maxNonEmptyCells} cells");
        return bestRow ?? worksheet.Row(1);
    }
    private string GetCellValueSafe(IXLCell cell)
    {
        try
        {
            if (cell.IsEmpty()) return "";

            if (cell.IsMerged())
            {
                var mergedRange = cell.MergedRange();
                cell = mergedRange.FirstCell();
            }

            if (cell.DataType == XLDataType.Text)
                return cell.GetText().Trim();
            else if (cell.DataType == XLDataType.Number)
                return cell.GetDouble().ToString().Trim();
            else if (cell.DataType == XLDataType.Boolean)
                return cell.GetBoolean().ToString().Trim();
            else
                return cell.Value.ToString()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }


    private string? GetCellValue(IXLRow row, int columnIndex)
    {
        if (columnIndex == 0) return null;

        try
        {
            var cell = row.Cell(columnIndex);
            if (cell.IsEmpty()) return null;

            if (cell.IsMerged())
            {
                cell = cell.MergedRange().FirstCell();
            }

            // ✅ CRITICAL: Handle numbers FIRST
            if (cell.DataType == XLDataType.Number)
            {
                var numValue = cell.GetDouble();

                // Return as integer if whole number
                if (numValue == Math.Floor(numValue))
                {
                    return ((long)numValue).ToString();
                }

                return numValue.ToString();
            }

            if (cell.DataType == XLDataType.DateTime)
            {
                try
                {
                    var dateTime = cell.GetDateTime();
                    return dateTime.ToString("yyyy-MM-dd");
                }
                catch
                {
                    return cell.GetText().Trim();
                }
            }

            if (cell.DataType == XLDataType.Text)
            {
                return cell.GetText().Trim();
            }

            if (cell.DataType == XLDataType.Boolean)
            {
                return cell.GetBoolean().ToString();
            }

            var value = cell.Value;

            return value.ToString()?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetCellValue] Error at column {columnIndex}: {ex.Message}");
            return null;
        }
    }

    private bool TryParseInt(string? value, out int result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();

        if (int.TryParse(value, out result))
            return true;

        if (double.TryParse(value, out double doubleResult))
        {
            result = (int)Math.Round(doubleResult);
            return true;
        }

        value = value.Replace(",", "").Replace(" ", "");

        if (int.TryParse(value, out result))
            return true;

        return false;
    }
    private DateOnly? ParseGregorianDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Clean up the input
        dateStr = dateStr.Trim();

        // Try direct DateOnly parse first (handles most formats)
        if (DateOnly.TryParse(dateStr, out DateOnly directResult))
        {
            return directResult;
        }

        // Comprehensive list of date formats
        string[] formats = {
        // Common formats with slashes
        "dd/MM/yyyy", "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy",
        "MM/dd/yyyy", "M/d/yyyy", "M/dd/yyyy", "MM/d/yyyy",
        "yyyy/MM/dd", "yyyy/M/d", "yyyy/MM/d", "yyyy/M/dd",
        
        // Common formats with dashes
        "dd-MM-yyyy", "d-M-yyyy", "dd-M-yyyy", "d-MM-yyyy",
        "MM-dd-yyyy", "M-d-yyyy", "M-dd-yyyy", "MM-d-yyyy",
        "yyyy-MM-dd", "yyyy-M-d", "yyyy-MM-d", "yyyy-M-dd",
        
        // Formats with dots
        "dd.MM.yyyy", "d.M.yyyy", "dd.M.yyyy", "d.MM.yyyy",
        "MM.dd.yyyy", "M.d.yyyy",
        "yyyy.MM.dd", "yyyy.M.d",
        
        // ISO 8601 formats
        "yyyy-MM-dd", "yyyyMMdd",
        
        // Month name formats
        "dd-MMM-yyyy", "d-MMM-yyyy",
        "dd MMM yyyy", "d MMM yyyy",
        "MMM dd, yyyy", "MMMM dd, yyyy",
        
        // Two-digit year formats
        "dd/MM/yy", "d/M/yy", "MM/dd/yy", "M/d/yy",
        "dd-MM-yy", "d-M-yy", "MM-dd-yy", "M-d-yy",
        "yy/MM/dd", "yy-MM-dd",
        
        // Additional common formats
        "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy hh:mm:ss tt", "MM/dd/yyyy hh:mm:ss tt"
    };

        // Try parsing with each format
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date))
            {
                return DateOnly.FromDateTime(date);
            }

            // Also try with current culture
            if (DateTime.TryParseExact(dateStr, format,
                CultureInfo.CurrentCulture,
                DateTimeStyles.None,
                out DateTime dateWithCulture))
            {
                return DateOnly.FromDateTime(dateWithCulture);
            }
        }

        // Try general parse as last resort
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime generalDate))
        {
            return DateOnly.FromDateTime(generalDate);
        }

        // Try with current culture
        if (DateTime.TryParse(dateStr, CultureInfo.CurrentCulture,
            DateTimeStyles.None, out DateTime generalDateCulture))
        {
            return DateOnly.FromDateTime(generalDateCulture);
        }

        return null;
    }

    private DateOnly? ParseHijriDate(string? hijriDateStr)
    {
        if (string.IsNullOrWhiteSpace(hijriDateStr))
            return null;

        try
        {
            var parts = hijriDateStr.Split('/', '-');
            if (parts.Length != 3)
                return null;

            if (!int.TryParse(parts[0], out int day) ||
                !int.TryParse(parts[1], out int month) ||
                !int.TryParse(parts[2], out int year))
                return null;

            if (year < 1300 || year > 1500 ||
                month < 1 || month > 12 ||
                day < 1 || day > 30)
                return null;

            var hijriCalendar = new HijriCalendar();

            int maxDays = hijriCalendar.GetDaysInMonth(year, month);
            if (day > maxDays)
                day = maxDays;

            var gregorianDate = hijriCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return DateOnly.FromDateTime(gregorianDate);
        }
        catch
        {
            return null;
        }
    }

    private async Task<(bool created, bool updated, string? error)> ProcessEmployee(
        RowData data,
        List<string> warnings)
    {
        try
        {
            var employee = await _dbcontext.Employees
                .FirstOrDefaultAsync(e => e.IqamaNo == data.IqamaNo);

            if (employee == null)
            {
                employee = new Employees
                {
                    IqamaNo = data.IqamaNo,
                    NameAR = data.NameAR!,
                    NameEN = data.NameEN!,
                    IqamaEndM = data.IqamaEndM!.Value,
                    IqamaEndH = data.IqamaEndH!.Value,
                    PassportNo = data.PassportNo,
                    PassportEnd = data.PassportEnd,
                    Sponsor = data.Sponsor!,
                    sponsorNo = data.SponsorNo,
                    JobTitle = data.JobTitle!,
                    Country = data.Country!,
                    Phone = data.Phone!,
                    DateOfBirth = data.DateOfBirth!.Value,
                    Status = data.Status!,
                    IBAN = data.IBAN,
                    INKSA = data.INKSA,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                };

                await _dbcontext.Employees.AddAsync(employee);
                await _dbcontext.SaveChangesAsync();

                return (true, false, null);
            }
            else
            {
                bool hasChanges = false;

                if (data.IqamaEndM.HasValue && employee.IqamaEndM != data.IqamaEndM.Value)
                {
                    employee.IqamaEndM = data.IqamaEndM.Value;
                    hasChanges = true;
                }

                if (data.IqamaEndH.HasValue && employee.IqamaEndH != data.IqamaEndH.Value)
                {
                    employee.IqamaEndH = data.IqamaEndH.Value;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.PassportNo) && employee.PassportNo != data.PassportNo)
                {
                    employee.PassportNo = data.PassportNo;
                    hasChanges = true;
                }

                if (data.PassportEnd.HasValue && employee.PassportEnd != data.PassportEnd)
                {
                    employee.PassportEnd = data.PassportEnd;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Sponsor) && employee.Sponsor != data.Sponsor)
                {
                    employee.Sponsor = data.Sponsor;
                    hasChanges = true;
                }

                if (data.SponsorNo != 0 && employee.sponsorNo != data.SponsorNo)
                {
                    employee.sponsorNo = data.SponsorNo;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.JobTitle) && employee.JobTitle != data.JobTitle)
                {
                    employee.JobTitle = data.JobTitle;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.NameAR) && employee.NameAR != data.NameAR)
                {
                    employee.NameAR = data.NameAR;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.NameEN) && employee.NameEN != data.NameEN)
                {
                    employee.NameEN = data.NameEN;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Country) && employee.Country != data.Country)
                {
                    employee.Country = data.Country;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Phone) && employee.Phone != data.Phone)
                {
                    employee.Phone = data.Phone;
                    hasChanges = true;
                }

                if (data.DateOfBirth.HasValue && employee.DateOfBirth != data.DateOfBirth.Value)
                {
                    employee.DateOfBirth = data.DateOfBirth.Value;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.Status) && employee.Status != data.Status)
                {
                    employee.Status = data.Status;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.IBAN) && employee.IBAN != data.IBAN)
                {
                    employee.IBAN = data.IBAN;
                    hasChanges = true;
                }

                employee.INKSA = data.INKSA;

                if (hasChanges)
                {
                    await _dbcontext.SaveChangesAsync();
                    warnings.Add("Employee record updated with new data");
                    return (false, true, null);
                }
                else
                {
                    warnings.Add("Employee exists with same data - no changes made");
                    return (false, false, null);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, false, $"Employee processing error: {ex.Message}");
        }
    }

    private async Task<(bool created, bool updated, string? error)> ProcessRider(
        RowData data,
        List<string> warnings)
    {
        try
        {
            if (!data.CompanyId.HasValue)
            {
                return (false, false, "Company ID not resolved");
            }

            var rider = await _dbcontext.RiderDetails
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == data.IqamaNo);

            if (rider == null)
            {
                if (string.IsNullOrWhiteSpace(data.WorkingId) && !data.CompanyId.HasValue)
                {
                    warnings.Add("No rider data provided - skipping rider creation");
                    return (false, false, null);
                }

                rider = new RiderDetails
                {
                    EmployeeIqamaNo = data.IqamaNo,
                    WorkingId = data.WorkingId,
                    TshirtSize = data.TshirtSize,
                    LicenseNumber = data.LicenseNumber,
                    CompanyId = data.CompanyId.Value,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                };

                await _dbcontext.RiderDetails.AddAsync(rider);
                await _dbcontext.SaveChangesAsync();

                return (true, false, null);
            }
            else
            {
                bool hasChanges = false;

                if (!string.IsNullOrWhiteSpace(data.WorkingId) && rider.WorkingId != data.WorkingId)
                {
                    rider.WorkingId = data.WorkingId;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.TshirtSize) && rider.TshirtSize != data.TshirtSize)
                {
                    rider.TshirtSize = data.TshirtSize;
                    hasChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(data.LicenseNumber) && rider.LicenseNumber != data.LicenseNumber)
                {
                    rider.LicenseNumber = data.LicenseNumber;
                    hasChanges = true;
                }

                if (data.CompanyId.HasValue && rider.CompanyId != data.CompanyId.Value)
                {
                    rider.CompanyId = data.CompanyId.Value;
                    hasChanges = true;
                    warnings.Add($"Rider company changed to {data.CompanyName}");
                }

                if (hasChanges)
                {
                    await _dbcontext.SaveChangesAsync();
                    warnings.Add("Rider record updated with new data");
                    return (false, true, null);
                }
                else
                {
                    warnings.Add("Rider exists with same data - no changes made");
                    return (false, false, null);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, false, $"Rider processing error: {ex.Message}");
        }
    }


    public async Task<Result<WorkingIdUpdateResponse>> UpdateRiderWorkingIdsAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<WorkingIdUpdateResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<WorkingIdUpdateResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<WorkingIdUpdateRowResult>();
        var errors = new List<string>();
        var notFoundIqamas = new List<string>();
        int successfulUpdates = 0;
        int failedRecords = 0;
        int iqamaNotFound = 0;
        int riderDetailsNotFound = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<WorkingIdUpdateResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindWorkingIdHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<WorkingIdUpdateResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            // Map columns by finding their positions
            var columnMap = BuildWorkingIdColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<WorkingIdUpdateResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();
                try
                {
                    var rowData = ParseWorkingIdRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new WorkingIdUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            rowData.NewWorkingId,
                            null,
                            null,
                            null,
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    // Find employee and rider details
                    var employee = await _dbcontext.Employees
                        .Include(e => e.RiderDetails)
                        .FirstOrDefaultAsync(e => e.IqamaNo == rowData.IqamaNo!.Value);

                    if (employee == null)
                    {
                        iqamaNotFound++;
                        failedRecords++;
                        notFoundIqamas.Add(rowData.IqamaNo!.Value.ToString());

                        results.Add(new WorkingIdUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NewWorkingId,
                            null,
                            null,
                            null,
                            "Employee with this Iqama number not found"
                        ));

                        await transaction.RollbackAsync();
                        continue;
                    }

                    if (employee.RiderDetails == null)
                    {
                        riderDetailsNotFound++;
                        failedRecords++;

                        results.Add(new WorkingIdUpdateRowResult(
                            rowNumber,
                            false,
                            rowData.IqamaNo!.Value.ToString(),
                            rowData.NewWorkingId,
                            null,
                            employee.NameEN,
                            employee.NameAR,
                            "Employee exists but has no RiderDetails record"
                        ));

                        await transaction.RollbackAsync();
                        continue;
                    }

                    // Update WorkingId
                    string? oldWorkingId = employee.RiderDetails.WorkingId;
                    employee.RiderDetails.WorkingId = rowData.NewWorkingId;

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulUpdates++;
                    results.Add(new WorkingIdUpdateRowResult(
                        rowNumber,
                        true,
                        rowData.IqamaNo!.Value.ToString(),
                        rowData.NewWorkingId,
                        oldWorkingId,
                        employee.NameEN,
                        employee.NameAR,
                        null
                    ));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new WorkingIdUpdateRowResult(
                        rowNumber,
                        false,
                        "N/A",
                        null,
                        null,
                        null,
                        null,
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            var response = new WorkingIdUpdateResponse(
                TotalRecords: dataRows.Count,
                SuccessfulUpdates: successfulUpdates,
                FailedRecords: failedRecords,
                IqamaNotFound: iqamaNotFound,
                RiderDetailsNotFound: riderDetailsNotFound,
                Results: results,
                NotFoundIqamas: notFoundIqamas,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<WorkingIdUpdateResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private IXLRow FindWorkingIdHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
        "رقم الاقامة", "رقم الإقامة", "الاقامة",
        "WorkingId", "Working Id", "Working ID",
        "معرف العمل", "معرف الشغل", "رقم العمل"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var nonEmptyCells = row.CellsUsed().Count(c =>
                !string.IsNullOrWhiteSpace(GetCellValueSafe(c)));

            if (nonEmptyCells >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private WorkingIdColumnMapping BuildWorkingIdColumnMapping(IXLRow headerRow)
    {
        var mapping = new WorkingIdColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
            "رقم الاقامة", "رقم الإقامة", "الاقامة");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working Id", "Working ID", "WorkingID",
            "معرف العمل", "معرف الشغل", "رقم العمل");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.WorkingIdCol == 0) missing.Add("Working ID");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private WorkingIdRowData ParseWorkingIdRowData(IXLRow row, WorkingIdColumnMapping map, int rowNumber)
    {
        var data = new WorkingIdRowData { RowNumber = rowNumber };

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            data.NewWorkingId = GetCellValue(row, map.WorkingIdCol);
            if (string.IsNullOrWhiteSpace(data.NewWorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "Working ID is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }


    internal class WorkingIdColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int WorkingIdCol { get; set; }
    }

    internal class WorkingIdRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
        public string? NewWorkingId { get; set; }
    }

    private IXLRow FindHousingHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
        "رقم الاقامة", "رقم الإقامة", "الاقامة",
        "HousingName", "Housing Name", "Housing", "السكن", "اسم السكن"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private HousingColumnMapping BuildHousingColumnMapping(IXLRow headerRow)
    {
        var mapping = new HousingColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "Iqama No",
            "رقم الاقامة", "رقم الإقامة", "الاقامة");

        mapping.HousingNameCol = FindColumn(cells,
            "HousingName", "Housing Name", "Housing",
            "السكن", "اسم السكن", "المسكن");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.HousingNameCol == 0) missing.Add("Housing Name");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private HousingRowData ParseHousingRowData(IXLRow row, HousingColumnMapping map, int rowNumber)
    {
        var data = new HousingRowData { RowNumber = rowNumber };

        try
        {
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            data.HousingName = GetCellValue(row, map.HousingNameCol);
            if (string.IsNullOrWhiteSpace(data.HousingName))
            {
                data.IsValid = false;
                data.ErrorMessage = "Housing Name is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    private IXLRow FindDeletedEmployeeHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة",
        "NameEN", "Name EN", "NameAR", "Name AR",
        "WorkingId", "Working ID", "معرف العمل"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private DeletedEmployeeColumnMapping BuildDeletedEmployeeColumnMapping(IXLRow headerRow)
    {
        var mapping = new DeletedEmployeeColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        // Map all columns (only IqamaNo is required)
        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة", "IqamaNo");

        mapping.NameARCol = FindColumn(cells,
            "NameAR", "Name AR", "الاسم بالعربية", "الاسم العربي", "Arabic Name");

        mapping.NameENCol = FindColumn(cells,
            "NameEN", "Name EN", "الاسم بالإنجليزية", "English Name", "Name");

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working ID", "معرف العمل", "WorkID", "رقم العمل", "المعرف");

        mapping.CompanyNameCol = FindColumn(cells,
            "CompanyName", "Company Name", "اسم الشركة", "الشركة");

        mapping.IqamaEndMCol = FindColumn(cells,
            "IqamaEndM", "Iqama End M", "تاريخ انتهاء الاقامة", "Iqama Expiry");

        mapping.IqamaEndHCol = FindColumn(cells,
            "IqamaEndH", "Iqama End H", "تاريخ انتهاء الاقامة هجري");

        mapping.PassportNoCol = FindColumn(cells,
            "PassportNo", "Passport Number", "رقم الجواز");

        mapping.PassportEndCol = FindColumn(cells,
            "PassportEnd", "Passport End", "تاريخ انتهاء الجواز");

        mapping.SponsorCol = FindColumn(cells,
            "Sponsor", "الكفيل", "اسم الكفيل");

        mapping.JobTitleCol = FindColumn(cells,
            "JobTitle", "Job Title", "المسمى الوظيفي", "الوظيفة");

        mapping.CountryCol = FindColumn(cells,
            "Country", "الجنسية", "البلد");

        mapping.PhoneCol = FindColumn(cells,
            "Phone", "رقم الجوال", "الجوال", "Mobile");

        mapping.DateOfBirthCol = FindColumn(cells,
            "DateOfBirth", "Date Of Birth", "تاريخ الميلاد");

        mapping.StatusCol = FindColumn(cells,
            "Status", "الحالة", "Employee Status");

        mapping.AcountStatusCol = FindColumn(cells,
            "AccountStatus", "حالة الحساب", "Account Status");

        mapping.IBANCol = FindColumn(cells,
            "IBAN", "رقم الآيبان", "الآيبان");

        mapping.INKSACol = FindColumn(cells,
            "INKSA", "في السعودية", "In KSA");

        mapping.TshirtSizeCol = FindColumn(cells,
            "TshirtSize", "Tshirt Size", "مقاس القميص");

        mapping.LicenseNumberCol = FindColumn(cells,
            "LicenseNumber", "License Number", "رقم الرخصة");

        // Only IqamaNo is required
        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required column missing: {string.Join(", ", missing)}\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private DeletedEmployeeRowData ParseDeletedEmployeeRowData(
        IXLRow row,
        DeletedEmployeeColumnMapping map,
        int rowNumber)
    {
        var data = new DeletedEmployeeRowData { RowNumber = rowNumber };

        try
        {
            // Parse IqamaNo (REQUIRED)
            var iqamaStr = GetCellValue(row, map.IqamaNoCol);
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr.Replace(" ", ""), out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            // Parse all optional fields
            data.NameAR = GetCellValue(row, map.NameARCol);
            data.NameEN = GetCellValue(row, map.NameENCol);
            data.WorkingId = GetCellValue(row, map.WorkingIdCol); // ~90% have this
            data.CompanyName = GetCellValue(row, map.CompanyNameCol);
            data.IqamaEndM = ParseGregorianDate(GetCellValue(row, map.IqamaEndMCol));
            data.IqamaEndH = ParseHijriDate(GetCellValue(row, map.IqamaEndHCol));
            data.PassportNo = GetCellValue(row, map.PassportNoCol);
            data.PassportEnd = ParseGregorianDate(GetCellValue(row, map.PassportEndCol));
            data.Sponsor = GetCellValue(row, map.SponsorCol);
            data.JobTitle = GetCellValue(row, map.JobTitleCol);
            data.Country = GetCellValue(row, map.CountryCol);
            data.Phone = GetCellValue(row, map.PhoneCol);
            data.Status = GetCellValue(row, map.StatusCol);
            data.AcountStatus = GetCellValue(row, map.AcountStatusCol);
            data.IBAN = GetCellValue(row, map.IBANCol);
            data.TshirtSize = GetCellValue(row, map.TshirtSizeCol);
            data.LicenseNumber = GetCellValue(row, map.LicenseNumberCol);

            // Parse DateOfBirth
            var dobStr = GetCellValue(row, map.DateOfBirthCol);
            if (!string.IsNullOrWhiteSpace(dobStr) && DateTime.TryParse(dobStr, out var dob))
            {
                data.DateOfBirth = dob;
            }

            // Parse INKSA
            var inksaStr = GetCellValue(row, map.INKSACol);
            data.INKSA = string.IsNullOrWhiteSpace(inksaStr) ||
                         inksaStr.ToLower() == "yes" ||
                         inksaStr == "1" ||
                         inksaStr.ToLower() == "true";

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ✅ Internal Classes for Deleted Employee Import

    internal class DeletedEmployeeColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        // Required
        public int IqamaNoCol { get; set; }

        // Optional
        public int NameARCol { get; set; }
        public int NameENCol { get; set; }
        public int WorkingIdCol { get; set; }
        public int CompanyNameCol { get; set; }
        public int IqamaEndMCol { get; set; }
        public int IqamaEndHCol { get; set; }
        public int PassportNoCol { get; set; }
        public int PassportEndCol { get; set; }
        public int SponsorCol { get; set; }
        public int JobTitleCol { get; set; }
        public int CountryCol { get; set; }
        public int PhoneCol { get; set; }
        public int DateOfBirthCol { get; set; }
        public int StatusCol { get; set; }
        public int AcountStatusCol { get; set; }
        public int IBANCol { get; set; }
        public int INKSACol { get; set; }
        public int TshirtSizeCol { get; set; }
        public int LicenseNumberCol { get; set; }
    }

    internal class DeletedEmployeeRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        // Required
        public long? IqamaNo { get; set; }

        // Optional
        public string? NameAR { get; set; }
        public string? NameEN { get; set; }
        public string? WorkingId { get; set; }
        public string? CompanyName { get; set; }
        public DateOnly? IqamaEndM { get; set; }
        public DateOnly? IqamaEndH { get; set; }
        public string? PassportNo { get; set; }
        public DateOnly? PassportEnd { get; set; }
        public string? Sponsor { get; set; }
        public string? JobTitle { get; set; }
        public string? Country { get; set; }
        public string? Phone { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Status { get; set; }
        public string? AcountStatus { get; set; }
        public string? IBAN { get; set; }
        public bool INKSA { get; set; } = true;
        public string? TshirtSize { get; set; }
        public string? LicenseNumber { get; set; }
    }

    // Add this method to ImportService.cs - Replace the existing ImportVehicleAssignmentsAsync method

    public async Task<Result<VehicleAssignmentImportResponse>> ImportVehicleAssignmentsAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleAssignmentImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleAssignmentImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleAssignmentRowResult>();
        var errors = new List<string>();
        int successfulAssignments = 0;
        int employeesConvertedToRiders = 0;
        int failedRecords = 0;
        int employeeNotFound = 0;
        int vehicleNotFound = 0;
        int vehicleReassigned = 0;
        int vehiclesReturned = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<VehicleAssignmentImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindVehicleAssignmentHeaderRow(worksheet);
            if (headerRow == null)
            {
                return Result.Failure<VehicleAssignmentImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildVehicleAssignmentColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<VehicleAssignmentImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            Console.WriteLine($"[VehicleAssignment] Total rows to process: {dataRows.Count}");

            // STEP 1: Parse all valid rows and collect vehicle assignments from Excel
            var excelAssignments = new Dictionary<string, (long IqamaNo, VehicleAssignmentRowData RowData)>();
            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var rowData = ParseVehicleAssignmentRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber, false,
                            rowData.IqamaNo?.ToString() ?? "N/A",
                            "N/A", "N/A",
                            rowData.VehicleNumber ?? "N/A", VehicleNumber: "N/A",  // ✅ Changed
                            false, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            new List<string>(),
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    // Normalize plate number
                    var cleanVehicleNumber = rowData.VehicleNumber!.Replace(" ", "").Trim().ToLower();  // ✅ Changed

                    // Store assignment (plate -> iqama mapping)
                    if (!excelAssignments.ContainsKey(cleanVehicleNumber))
                    {
                        excelAssignments[cleanVehicleNumber] = (rowData.IqamaNo!.Value, rowData);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {rowNumber} parsing error: {ex.Message}");
                }
            }

            Console.WriteLine($"[VehicleAssignment] Valid assignments in Excel: {excelAssignments.Count}");

            // STEP 2: Get all vehicles currently assigned to riders in the database
            var currentAssignments = await _dbcontext.RiderDetails
                .Where(r => !string.IsNullOrEmpty(r.VehicleNumber))
                .Include(r => r.Employee)
                .Include(r => r.Vehicle)
                .Select(r => new
                {
                    r.EmployeeIqamaNo,
                    r.VehicleNumber,
                    PlateNumberA = r.Vehicle!.PlateNumberA,
                    EmployeeNameEN = r.Employee.NameEN,
                    EmployeeNameAR = r.Employee.NameAR
                })
                .AsNoTracking()
                .ToListAsync();

            Console.WriteLine($"[VehicleAssignment] Current vehicle assignments in DB: {currentAssignments.Count}");

            // STEP 3: Return vehicles that are NOT in the Excel (system cleanup)
            foreach (var current in currentAssignments)
            {
                var cleanPlateNumber = current.PlateNumberA.Replace(" ", "").Trim().ToLower();

                // If this vehicle is NOT in Excel, it should be returned
                if (!excelAssignments.ContainsKey(cleanPlateNumber))
                {
                    using var transaction = await _dbcontext.Database.BeginTransactionAsync();

                    try
                    {
                        Console.WriteLine($"[VehicleAssignment] Returning vehicle {current.VehicleNumber} from rider {current.EmployeeIqamaNo} (not in Excel)");

                        // Find rider details
                        var rider = await _dbcontext.RiderDetails
                            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == current.EmployeeIqamaNo);

                        if (rider != null)
                        {
                            // Deactivate current "Taken" status
                            var takenStatus = await _dbcontext.RiderVehicleStatus
                                .FirstOrDefaultAsync(s =>
                                    s.VehicleNumber == current.VehicleNumber &&
                                    s.EmployeeIqamaNo == current.EmployeeIqamaNo &&
                                    s.IsActive &&
                                    s.StatusType == VehicleStatusType.Taken);

                            if (takenStatus != null)
                            {
                                takenStatus.IsActive = false;
                                takenStatus.PermissionEndDate = DateTime.UtcNow.AddHours(3);
                            }

                            // Add "Returned" status
                            _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                            {
                                VehicleNumber = current.VehicleNumber,
                                EmployeeIqamaNo = current.EmployeeIqamaNo,
                                StatusType = VehicleStatusType.Returned,
                                Reason = $"Auto-returned by system sync - Not in import sheet (by {uploadedBy})",
                                IsActive = false,
                                Timestamp = DateTime.UtcNow.AddHours(3)
                            });

                            // Remove vehicle from rider
                            rider.VehicleNumber = null;

                            await _dbcontext.SaveChangesAsync();
                            await transaction.CommitAsync();

                            vehiclesReturned++;

                            Console.WriteLine($"[VehicleAssignment] ✓ Returned vehicle {current.VehicleNumber} from {current.EmployeeNameEN}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        errors.Add($"Failed to return vehicle {current.VehicleNumber} from {current.EmployeeIqamaNo}: {ex.Message}");
                    }
                }
            }

            // STEP 4: Process each assignment from Excel
            rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                var rowData = ParseVehicleAssignmentRowData(row, columnMap, rowNumber);

                if (!rowData.IsValid)
                    continue; // Already handled in STEP 1

                using var transaction = await _dbcontext.Database.BeginTransactionAsync();

                try
                {
                    var warnings = new List<string>();
                    var cleanIqamaNo = rowData.IqamaNo!.Value;
                    var cleanVehicleNumber = rowData.VehicleNumber!.Replace(" ", "").Trim().ToLower();  // ✅ Changed

                    // Find employee
                    var employee = await _dbcontext.Employees
                        .Include(e => e.RiderDetails)
                            .ThenInclude(rd => rd.Company)
                        .Include(e => e.Housing)
                        .FirstOrDefaultAsync(e => e.IqamaNo == cleanIqamaNo);

                    if (employee == null)
                    {
                        employeeNotFound++;
                        failedRecords++;
                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber, false,
                            cleanIqamaNo.ToString(),
                            "N/A", "N/A",
                            rowData.VehicleNumber ?? "N/A", VehicleNumber: "N/A",  // ✅ Changed
                            false, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            warnings,
                            "Employee with this Iqama number not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(rowData.Phone))
                    {
                        if (employee.Phone != rowData.Phone)
                        {
                            warnings.Add($"Phone updated from '{employee.Phone}' to '{rowData.Phone}'");
                            employee.Phone = rowData.Phone;
                        }
                    }

                    bool wasConvertedToRider = false;

                    // Convert employee to rider if needed
                    if (employee.RiderDetails == null)
                    {
                        var defaultCompany = await _dbcontext.Companies
                            .OrderBy(c => c.Id)
                            .FirstOrDefaultAsync();

                        if (defaultCompany == null)
                        {
                            failedRecords++;
                            results.Add(new VehicleAssignmentRowResult(
                                rowNumber, false,
                                cleanIqamaNo.ToString(),
                                employee.NameEN, employee.NameAR,
                                 rowData.VehicleNumber ?? "N/A", VehicleNumber: "N/A",  // ✅ Changed
                                false, false,
                                null, null,
                                rowData.Permission,
                                rowData.PermissionStartDate,
                                rowData.PermissionEndDate,
                                warnings,
                                "No company found - cannot create rider"
                            ));
                            await transaction.RollbackAsync();
                            continue;
                        }

                        var newRider = new RiderDetails
                        {
                            EmployeeIqamaNo = employee.IqamaNo,
                            WorkingId = $"AUTO_{employee.IqamaNo}",
                            TshirtSize = "M",
                            LicenseNumber = "N/A",
                            CompanyId = defaultCompany.Id,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        await _dbcontext.RiderDetails.AddAsync(newRider);
                        await _dbcontext.SaveChangesAsync();

                        employee.RiderDetails = newRider;
                        wasConvertedToRider = true;
                        employeesConvertedToRiders++;
                        warnings.Add($"Auto-converted to rider with WorkingId: {newRider.WorkingId}");
                    }

                    // Find vehicle
                    var vehicle = await _dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber.Replace(" ", "") == cleanVehicleNumber);  // ✅ Changed from PlateNumberA


                    if (vehicle == null)
                    {
                        vehicleNotFound++;
                        failedRecords++;
                        results.Add(new VehicleAssignmentRowResult(
                            rowNumber, false,
                            cleanIqamaNo.ToString(),
                            employee.NameEN, employee.NameAR,
                            rowData.VehicleNumber ?? "N/A", VehicleNumber: "N/A",  // ✅ Changed
                            wasConvertedToRider, false,
                            null, null,
                            rowData.Permission,
                            rowData.PermissionStartDate,
                            rowData.PermissionEndDate,
                            warnings,
                            $"Vehicle with plate '{cleanVehicleNumber}' not found"
                        ));
                        await transaction.RollbackAsync();
                        continue;
                    }

                    // CRITICAL: Check if vehicle is currently with ANOTHER rider
                    var currentRider = await _dbcontext.RiderDetails
                        .Include(r => r.Employee)
                        .FirstOrDefaultAsync(r => r.VehicleNumber == vehicle.VehicleNumber);

                    if (currentRider != null && currentRider.EmployeeIqamaNo != cleanIqamaNo)
                    {
                        // REASSIGNMENT: Vehicle is with different rider - return it first
                        warnings.Add($"Vehicle reassigned from {currentRider.Employee.NameEN} to {employee.NameEN}");
                        vehicleReassigned++;

                        // Deactivate old rider's "Taken" status
                        var oldTakenStatus = await _dbcontext.RiderVehicleStatus
                            .FirstOrDefaultAsync(s =>
                                s.VehicleNumber == vehicle.VehicleNumber &&
                                s.EmployeeIqamaNo == currentRider.EmployeeIqamaNo &&
                                s.IsActive &&
                                s.StatusType == VehicleStatusType.Taken);

                        if (oldTakenStatus != null)
                        {
                            oldTakenStatus.IsActive = false;
                            oldTakenStatus.PermissionEndDate = DateTime.UtcNow.AddHours(3);
                        }

                        // Add "Returned" status for old rider
                        _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                        {
                            VehicleNumber = vehicle.VehicleNumber,
                            EmployeeIqamaNo = currentRider.EmployeeIqamaNo,
                            StatusType = VehicleStatusType.Returned,
                            Reason = $"Reassigned to another rider (by {uploadedBy})",
                            IsActive = false,
                            Timestamp = DateTime.UtcNow.AddHours(3)
                        });

                        // Remove vehicle from old rider
                        currentRider.VehicleNumber = null;

                        Console.WriteLine($"[VehicleAssignment] Reassigned vehicle {vehicle.VehicleNumber} from {currentRider.Employee.NameEN} to {employee.NameEN}");
                    }

                    // Handle if new employee/rider already has a different vehicle
                    if (!string.IsNullOrEmpty(employee.RiderDetails.VehicleNumber) &&
                        employee.RiderDetails.VehicleNumber != vehicle.VehicleNumber)
                    {
                        warnings.Add($"Rider's old vehicle {employee.RiderDetails.VehicleNumber} returned");

                        // Deactivate old vehicle's "Taken" status
                        var oldVehicleStatus = await _dbcontext.RiderVehicleStatus
                            .FirstOrDefaultAsync(s =>
                                s.VehicleNumber == employee.RiderDetails.VehicleNumber &&
                                s.EmployeeIqamaNo == employee.IqamaNo &&
                                s.IsActive &&
                                s.StatusType == VehicleStatusType.Taken);

                        if (oldVehicleStatus != null)
                        {
                            oldVehicleStatus.IsActive = false;
                            oldVehicleStatus.PermissionEndDate = DateTime.UtcNow.AddHours(3);
                        }

                        // Add "Returned" status for old vehicle
                        _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                        {
                            VehicleNumber = employee.RiderDetails.VehicleNumber,
                            EmployeeIqamaNo = employee.IqamaNo,
                            StatusType = VehicleStatusType.Returned,
                            Reason = $"Replaced with new vehicle (by {uploadedBy})",
                            IsActive = false,
                            Timestamp = DateTime.UtcNow.AddHours(3)
                        });
                    }

                    // Deactivate ALL old statuses for this vehicle (Problem, Stolen, etc.)
                    var oldStatuses = await _dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == vehicle.VehicleNumber && s.IsActive)
                        .ToListAsync();

                    foreach (var status in oldStatuses)
                    {
                        status.IsActive = false;
                        if (status.StatusType != VehicleStatusType.Returned)
                        {
                            warnings.Add($"Cleared vehicle status: {status.StatusType}");
                        }
                    }

                    // Handle permission
                    string finalPermission = rowData.Permission ?? "تصريح عام";

                    if (!string.IsNullOrWhiteSpace(rowData.Permission) &&
                        rowData.Permission.Contains("مرور"))
                    {
                        finalPermission = "تصريح مرور";
                        rowData.PermissionStartDate ??= DateTime.UtcNow.AddHours(3);
                        rowData.PermissionEndDate ??= DateTime.UtcNow.AddHours(3).AddDays(30);
                        warnings.Add("Traffic permission - 30-day default period");
                    }

                    // Update vehicle location
                    string previousLocation = vehicle.Location;
                    string newLocation = employee.Housing?.Name ?? "غير محدد";
                    vehicle.Location = newLocation;

                    // Assign vehicle to rider
                    employee.RiderDetails.VehicleNumber = vehicle.VehicleNumber;

                    // Create "Taken" status
                    _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
                    {
                        VehicleNumber = vehicle.VehicleNumber,
                        EmployeeIqamaNo = employee.IqamaNo,
                        StatusType = VehicleStatusType.Taken,
                        Reason = $"Assigned via bulk import (by {uploadedBy})",
                        IsActive = true,
                        Permission = finalPermission,
                        PermissionStartDate = rowData.PermissionStartDate ?? DateTime.UtcNow.AddHours(3),
                        PermissionEndDate = rowData.PermissionEndDate,
                        Timestamp = DateTime.UtcNow.AddHours(3)
                    });

                    await _dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    successfulAssignments++;
                    results.Add(new VehicleAssignmentRowResult(
                        rowNumber, true,
                        cleanIqamaNo.ToString(),
                        employee.NameEN, employee.NameAR,
                        cleanVehicleNumber,
                        vehicle.VehicleNumber,
                        wasConvertedToRider,
                        true,
                        previousLocation,
                        newLocation,
                        finalPermission,
                        rowData.PermissionStartDate,
                        rowData.PermissionEndDate,
                        warnings,
                        null
                    ));

                    Console.WriteLine($"[VehicleAssignment] ✓ Assigned {vehicle.VehicleNumber} to {employee.NameEN}");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new VehicleAssignmentRowResult(
                        rowNumber, false,
                        "N/A", "N/A", "N/A",
                        "N/A", "N/A",
                        false, false,
                        null, null,
                        null, null, null,
                        new List<string>(),
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            Console.WriteLine($"[VehicleAssignment] Import complete:");
            Console.WriteLine($"  - Successful: {successfulAssignments}");
            Console.WriteLine($"  - Reassigned: {vehicleReassigned}");
            Console.WriteLine($"  - Returned (not in Excel): {vehiclesReturned}");
            Console.WriteLine($"  - Failed: {failedRecords}");

            var response = new VehicleAssignmentImportResponse(
                TotalRecords: dataRows.Count,
                SuccessfulAssignments: successfulAssignments,
                EmployeesConvertedToRiders: employeesConvertedToRiders,
                FailedRecords: failedRecords,
                EmployeeNotFound: employeeNotFound,
                VehicleNotFound: vehicleNotFound,
                VehicleUnavailable: vehicleReassigned, // Vehicles reassigned from one rider to another
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            // Add summary message about system sync
            if (vehiclesReturned > 0)
            {
                errors.Insert(0, $"=== SYSTEM SYNC SUMMARY ===");
                errors.Insert(1, $"Vehicles auto-returned (not in Excel): {vehiclesReturned}");
                errors.Insert(2, $"Vehicles reassigned to different riders: {vehicleReassigned}");
                errors.Insert(3, $"Total successful assignments from Excel: {successfulAssignments}");
            }

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VehicleAssignment] FATAL ERROR: {ex}");
            return Result.Failure<VehicleAssignmentImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }
    private IXLRow FindVehicleAssignmentHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "IqamaNumber", "Iqama Number", "رقم الاقامة", "رقم الإقامة",
        "VehicleNumber", "Vehicle Number", "رقم المركبة", "رقم الهيكل",  // ✅ Changed
        "Permission", "التصريح", "الصلاحية",
        "PermissionStartDate", "تاريخ بداية التصريح",
        "PermissionEndDate", "تاريخ نهاية التصريح"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private VehicleAssignmentColumnMapping BuildVehicleAssignmentColumnMapping(IXLRow headerRow)
    {
        var mapping = new VehicleAssignmentColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.IqamaNoCol = FindColumn(cells,
            "IqamaNumber", "Iqama Number", "IqamaNo", "رقم الاقامة", "رقم الإقامة");

        mapping.VehicleNumberCol = FindColumn(cells,  // ✅ Changed
                  "VehicleNumber", "Vehicle Number", "رقم المركبة", "رقم الهيكل");

        mapping.PermissionCol = FindColumn(cells,
            "Permission", "التصريح", "الصلاحية", "نوع التصريح");

        mapping.PermissionStartDateCol = FindColumn(cells,
            "PermissionStartDate", "Permission Start Date", "تاريخ بداية التصريح", "تاريخ البداية", "بداية التصريح");

        mapping.PermissionEndDateCol = FindColumn(cells,
            "PermissionEndDate", "Permission End Date", "تاريخ نهاية التصريح", "تاريخ النهاية", "نهاية التصريح");

        mapping.PhoneCol = FindColumn(cells,
             "PhoneNumber", "PhoneNumber", "رقم الجوال", "الجوال", "Mobile");

        var missing = new List<string>();
        if (mapping.IqamaNoCol == 0) missing.Add("Iqama Number");
        if (mapping.VehicleNumberCol == 0) missing.Add("Vehicle Number");  // ✅ Changed

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private VehicleAssignmentRowData ParseVehicleAssignmentRowData(
        IXLRow row,
        VehicleAssignmentColumnMapping map,
        int rowNumber)
    {
        var data = new VehicleAssignmentRowData { RowNumber = rowNumber };

        try
        {
            // Parse and trim IqamaNo
            var iqamaStr = GetCellValue(row, map.IqamaNoCol)?.Replace(" ", "").Trim();
            if (string.IsNullOrWhiteSpace(iqamaStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Iqama Number is required";
                return data;
            }

            if (!long.TryParse(iqamaStr, out long iqamaNo) || iqamaNo <= 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid Iqama Number: {iqamaStr}";
                return data;
            }
            data.IqamaNo = iqamaNo;

            // Parse and trim PlateNumberA
            data.VehicleNumber = GetCellValue(row, map.VehicleNumberCol)?.Replace(" ", "").Trim();
            if (string.IsNullOrWhiteSpace(data.VehicleNumber))
            {
                data.IsValid = false;
                data.ErrorMessage = "Vehicle Number is required";
                return data;
            }

            // Parse optional fields
            data.Permission = GetCellValue(row, map.PermissionCol)?.Trim();

            data.Phone = GetCellValue(row, map.PhoneCol)?.Trim();

            // Parse dates
            var startDateStr = GetCellValue(row, map.PermissionStartDateCol);
            if (!string.IsNullOrWhiteSpace(startDateStr))
            {
                if (DateTime.TryParse(startDateStr, out DateTime startDate))
                {
                    data.PermissionStartDate = startDate;
                }
            }

            var endDateStr = GetCellValue(row, map.PermissionEndDateCol);
            if (!string.IsNullOrWhiteSpace(endDateStr))
            {
                if (DateTime.TryParse(endDateStr, out DateTime endDate))
                {
                    data.PermissionEndDate = endDate;
                }
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    internal class VehicleAssignmentColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int VehicleNumberCol { get; set; }
        public int PermissionCol { get; set; }
        public int PermissionStartDateCol { get; set; }
        public int PermissionEndDateCol { get; set; }
        public int PhoneCol { get; set; }  // ✅ ADD THIS

    }

    internal class VehicleAssignmentRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
        public string? VehicleNumber { get; set; }
        public string? Permission { get; set; }
        public DateTime? PermissionStartDate { get; set; }
        public DateTime? PermissionEndDate { get; set; }
        public string? Phone { get; set; }  // ✅ ADD THIS

    }


    public async Task<Result<VehicleUsageCheckResponse>> CheckVehicleUsageFromExcelAsync(
        IFormFile file,
        string uploadedBy)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<VehicleUsageCheckResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<VehicleUsageCheckResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<VehicleUsageRowResult>();
        var errors = new List<VehicleUsageError>();
        int vehiclesInUse = 0;
        int vehiclesAvailable = 0;
        int vehiclesNotFound = 0;
        int failedRecords = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                return Result.Failure<VehicleUsageCheckResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            var headerRow = FindVehicleUsageHeaderRow(worksheet);

            if (headerRow == null)
            {
                return Result.Failure<VehicleUsageCheckResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            var columnMap = BuildVehicleUsageColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                return Result.Failure<VehicleUsageCheckResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var rowData = ParseVehicleUsageRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        errors.Add(new VehicleUsageError(
                            rowNumber,
                            rowData.PlateNumberA ?? "N/A",
                            "ValidationError",
                            rowData.ErrorMessage!
                        ));
                        continue;
                    }

                    var warnings = new List<string>();

                    // Normalize plate number - remove all spaces
                    var normalizedPlateNumber = rowData.PlateNumberA!.Replace(" ", "").Trim();

                    // Find vehicle with rider details
                    var vehicle = await _dbcontext.Vehicles
                        .Include(v => v.RiderDetails)
                            .ThenInclude(rd => rd.Employee)
                        .Include(v => v.RiderDetails)
                            .ThenInclude(rd => rd.Company)
                        .FirstOrDefaultAsync(v => v.PlateNumberA.Replace(" ", "") == normalizedPlateNumber);

                    if (vehicle == null)
                    {
                        vehiclesNotFound++;
                        results.Add(new VehicleUsageRowResult(
                            rowNumber,
                            true,
                            rowData.PlateNumberA!,
                            "N/A",
                            "N/A",
                            VehicleUsageStatus.NotFound,
                            null,
                            warnings
                        ));
                        continue;
                    }

                    // Check if vehicle is assigned to a rider
                    if (vehicle.RiderDetails != null)
                    {
                        var employee = vehicle.RiderDetails.Employee;

                        // Validation warnings
                        if (employee.Status != "enable")
                        {
                            warnings.Add($"Rider status is '{employee.Status}' (not enabled)");
                        }

                        if (string.IsNullOrWhiteSpace(vehicle.RiderDetails.WorkingId))
                        {
                            warnings.Add("Rider has no Working ID assigned");
                        }

                        vehiclesInUse++;
                        results.Add(new VehicleUsageRowResult(
                            rowNumber,
                            true,
                            vehicle.PlateNumberA,
                            vehicle.VehicleNumber,
                            vehicle.VehicleType,
                            VehicleUsageStatus.InUse,
                            new RiderUsageInfo(
                                employee.IqamaNo,
                                employee.NameAR,
                                employee.NameEN,
                                vehicle.RiderDetails.WorkingId,
                                vehicle.RiderDetails.Company?.Name ?? "N/A"
                            ),
                            warnings
                        ));
                    }
                    else
                    {
                        vehiclesAvailable++;
                        results.Add(new VehicleUsageRowResult(
                            rowNumber,
                            true,
                            vehicle.PlateNumberA,
                            vehicle.VehicleNumber,
                            vehicle.VehicleType,
                            VehicleUsageStatus.Available,
                            null,
                            warnings
                        ));
                    }
                }
                catch (Exception ex)
                {
                    failedRecords++;
                    errors.Add(new VehicleUsageError(
                        rowNumber,
                        "N/A",
                        "ProcessingError",
                        $"Unexpected error: {ex.Message}"
                    ));
                }
            }

            var response = new VehicleUsageCheckResponse(
                TotalVehicles: dataRows.Count,
                VehiclesInUse: vehiclesInUse,
                VehiclesAvailable: vehiclesAvailable,
                VehiclesNotFound: vehiclesNotFound,
                FailedRecords: failedRecords,
                Results: results,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleUsageCheckResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    private IXLRow FindVehicleUsageHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "PlateNumber", "Plate Number", "PlateNumberA", "رقم اللوحة",
        "اللوحة", "اللوحة العربية", "Plate A", "Arabic Plate"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = new List<string>();

            foreach (var cell in row.CellsUsed())
            {
                try
                {
                    string value = cell.IsMerged()
                        ? cell.MergedRange().FirstCell().GetString().Trim()
                        : cell.GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(value))
                        cellValues.Add(value);
                }
                catch { }
            }

            int matchCount = 0;
            foreach (var cellValue in cellValues)
            {
                foreach (var knownCol in knownColumns)
                {
                    if (cellValue.Equals(knownCol, StringComparison.OrdinalIgnoreCase) ||
                        cellValue.Replace(" ", "").Equals(knownCol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        matchCount++;
                        break;
                    }
                }
            }

            if (matchCount >= 1)
                return row;
        }

        // Fallback to row 1
        return worksheet.Row(1);
    }

    private VehicleUsageColumnMapping BuildVehicleUsageColumnMapping(IXLRow headerRow)
    {
        var mapping = new VehicleUsageColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        var actualHeaders = new List<string>();
        foreach (var cell in cells)
        {
            try
            {
                string val = cell.IsMerged()
                    ? cell.MergedRange().FirstCell().GetString()
                    : cell.GetString();
                actualHeaders.Add($"Col{cell.Address.ColumnNumber}({cell.Address.ColumnLetter})='{val}'");
            }
            catch { }
        }

        mapping.PlateNumberACol = FindColumn(cells,
            "PlateNumber", "Plate Number", "PlateNumberA", "Plate Number A",
            "رقم اللوحة", "اللوحة", "اللوحة العربية", "Plate A", "Arabic Plate");

        if (mapping.PlateNumberACol == 0)
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = "Required column 'Plate Number' not found\n" +
                                  $"Columns found:\n{string.Join("\n", actualHeaders)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private VehicleUsageRowData ParseVehicleUsageRowData(
        IXLRow row,
        VehicleUsageColumnMapping map,
        int rowNumber)
    {
        var data = new VehicleUsageRowData { RowNumber = rowNumber };

        try
        {
            data.PlateNumberA = GetCellValue(row, map.PlateNumberACol)?.Trim();

            if (string.IsNullOrWhiteSpace(data.PlateNumberA))
            {
                data.IsValid = false;
                data.ErrorMessage = "Plate Number is required";
                return data;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // Internal classes for Vehicle Usage Check
    internal class VehicleUsageColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int PlateNumberACol { get; set; }
    }

    internal class VehicleUsageRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PlateNumberA { get; set; }
    }


    // ============================================
    // ADD THIS METHOD TO YOUR ImportService.cs
    // ============================================

    public async Task<Result<RiderShiftBulkImportResponse>> BulkImportRiderShiftsAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<RiderShiftBulkImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<RiderShiftBulkImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var details = new List<RiderShiftImportDetail>();
        var errors = new List<string>();

        int successfulShifts = 0;
        int updatedShifts = 0;
        int skippedDuplicates = 0;
        int workingIdNotFound = 0;
        int housingNotFound = 0;
        int validationErrors = 0;

        try
        {
            Console.WriteLine($"[BulkImportShifts] Starting import for file: {file.FileName}");

            // Load rider lookup data into memory for fast access
            Console.WriteLine("[BulkImportShifts] Loading rider details lookup...");
            var riderLookup = await LoadRiderLookupForShifts();
            Console.WriteLine($"[BulkImportShifts] Loaded {riderLookup.Count} rider entries");

            // Load housing lookup
            Console.WriteLine("[BulkImportShifts] Loading housing lookup...");
            var housingLookup = await LoadHousingLookup();
            Console.WriteLine($"[BulkImportShifts] Loaded {housingLookup.Count} housing entries");

            using var stream = file.OpenReadStream();
            Console.WriteLine($"[BulkImportShifts] File stream opened, length: {stream.Length} bytes");

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[BulkImportShifts] ERROR: Could not read worksheet");
                return Result.Failure<RiderShiftBulkImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[BulkImportShifts] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindShiftImportHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[BulkImportShifts] ERROR: No header row found");
                return Result.Failure<RiderShiftBulkImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[BulkImportShifts] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildShiftImportColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[BulkImportShifts] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<RiderShiftBulkImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine($"[BulkImportShifts] Column mapping successful");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[BulkImportShifts] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[BulkImportShifts] WARNING: No data rows found");
                return Result.Failure<RiderShiftBulkImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            // Report initial progress
            try
            {
                progressCallback?.Invoke(0, totalRows);
                Console.WriteLine($"[BulkImportShifts] Initial progress callback sent: 0/{totalRows}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BulkImportShifts] ERROR in progress callback: {ex.Message}");
            }

            // Track duplicates within this import to skip them
            var processedShifts = new HashSet<string>();

            // Batch processing for better performance
            const int BATCH_SIZE = 1000;
            var shiftsToInsert = new List<RiderShift>();
            var shiftsToUpdate = new List<RiderShift>();

            var rowNumber = headerRow.RowNumber();
            int processedCount = 0;

            foreach (var row in dataRows)
            {
                rowNumber++;
                processedCount++;

                try
                {
                    var rowData = ParseShiftImportRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        validationErrors++;
                        // ✅ ADD TO DETAILS (This is an error)
                        details.Add(new RiderShiftImportDetail(
                            rowNumber,
                            rowData.WorkingId ?? "N/A",
                            rowData.ShiftDate ?? DateOnly.MinValue,
                            ImportStatus.ValidationError,
                            null, null, null, null, null,
                            rowData.AcceptedOrders,
                            rowData.ShiftStatus ?? "N/A",
                            rowData.ErrorMessage
                        ));
                        continue;
                    }

                    // Check for duplicates within this import
                    var shiftKey = $"{rowData.WorkingId}_{rowData.ShiftDate}";
                    if (processedShifts.Contains(shiftKey))
                    {
                        skippedDuplicates++;
                        // ✅ ADD TO DETAILS (This is an error)
                        details.Add(new RiderShiftImportDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.ShiftDate!.Value,
                            ImportStatus.SkippedDuplicate,
                            "Duplicate in Excel",
                            null, null, null,
                            rowData.HousingId,
                            rowData.AcceptedOrders,
                            rowData.ShiftStatus!,
                            "Duplicate WorkingId + ShiftDate"
                        ));
                        continue;
                    }

                    processedShifts.Add(shiftKey);

                    // Find rider by WorkingId
                    var workingIdKey = rowData.WorkingId!.Trim().ToLower();

                    if (!riderLookup.TryGetValue(workingIdKey, out var riderInfo))
                    {
                        workingIdNotFound++;
                        // ✅ ADD TO DETAILS (This is an error)
                        details.Add(new RiderShiftImportDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.ShiftDate!.Value,
                            ImportStatus.WorkingIdNotFound,
                            null, null, null, null,
                            rowData.HousingId,
                            rowData.AcceptedOrders,
                            rowData.ShiftStatus!,
                            "WorkingId not found"
                        ));
                        continue;
                    }

                    // Validate housing if provided
                    if (rowData.HousingId.HasValue && !housingLookup.ContainsKey(rowData.HousingId.Value))
                    {
                        housingNotFound++;
                        // ✅ ADD TO DETAILS (This is an error)
                        details.Add(new RiderShiftImportDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.ShiftDate!.Value,
                            ImportStatus.HousingNotFound,
                            null,
                            riderInfo.RiderId,
                            riderInfo.IqamaNo,
                            riderInfo.Source,
                            rowData.HousingId,
                            rowData.AcceptedOrders,
                            rowData.ShiftStatus!,
                            $"Housing ID {rowData.HousingId} not found"
                        ));
                        continue;
                    }

                    // Check if shift already exists in database
                    var existingShift = await _dbcontext.RiderShifts
                        .FirstOrDefaultAsync(s =>
                            s.RiderId == riderInfo.RiderId &&
                            s.ShiftDate == rowData.ShiftDate!.Value);

                    if (existingShift != null)
                    {
                        // Update existing shift
                        existingShift.AcceptedDailyOrders = rowData.AcceptedOrders;
                        existingShift.ShiftStatus = rowData.ShiftStatus!;
                        existingShift.HousingId = rowData.HousingId;
                        existingShift.WorkingId = rowData.WorkingId!;

                        shiftsToUpdate.Add(existingShift);
                        updatedShifts++;

                        details.Add(new RiderShiftImportDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.ShiftDate!.Value,
                            ImportStatus.Updated,
                            "Updated existing shift",
                            riderInfo.RiderId,
                            riderInfo.IqamaNo,
                            riderInfo.Source,
                            rowData.HousingId,
                            rowData.AcceptedOrders,
                            rowData.ShiftStatus!,
                            null
                        ));
                    }
                    else
                    {
                        // Create new shift
                        var newShift = new RiderShift
                        {
                            RiderId = riderInfo.RiderId,
                            WorkingId = rowData.WorkingId!,
                            ShiftDate = rowData.ShiftDate!.Value,
                            AcceptedDailyOrders = rowData.AcceptedOrders,
                            RejectedDailyOrders = 0,
                            StackedDeliveries = 0,
                            RealRejectedDailyOrders = 0,
                            HousingId = rowData.HousingId,
                            WorkingHours = 9,
                            CompanyId = 1,
                            ShiftStatus = rowData.ShiftStatus!,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        shiftsToInsert.Add(newShift);
                        successfulShifts++;

                        details.Add(new RiderShiftImportDetail(
                            rowNumber,
                            rowData.WorkingId!,
                            rowData.ShiftDate!.Value,
                            ImportStatus.Success,
                            "Created new shift",
                            riderInfo.RiderId,
                            riderInfo.IqamaNo,
                            riderInfo.Source,
                            rowData.HousingId,
                            rowData.AcceptedOrders,
                            rowData.ShiftStatus!,
                            null
                        ));
                    }

                    // Batch save every 1000 records
                    if (shiftsToInsert.Count >= BATCH_SIZE)
                    {
                        await _dbcontext.RiderShifts.AddRangeAsync(shiftsToInsert);
                        await _dbcontext.SaveChangesAsync();
                        Console.WriteLine($"[BulkImportShifts] Saved batch of {shiftsToInsert.Count} new shifts");
                        shiftsToInsert.Clear();
                    }

                    if (shiftsToUpdate.Count >= BATCH_SIZE)
                    {
                        await _dbcontext.SaveChangesAsync();
                        Console.WriteLine($"[BulkImportShifts] Updated batch of {shiftsToUpdate.Count} shifts");
                        shiftsToUpdate.Clear();
                    }
                }
                catch (Exception ex)
                {
                    validationErrors++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    // ✅ ADD TO DETAILS (This is an error)
                    details.Add(new RiderShiftImportDetail(
                        rowNumber,
                        "N/A",
                        DateOnly.MinValue,
                        ImportStatus.ValidationError,
                        null, null, null, null, null,
                        0,
                        "N/A",
                        $"Processing error: {ex.Message}"
                    ));
                }

                // Report progress every 500 rows
                if (processedCount % 500 == 0)
                {
                    try
                    {
                        progressCallback?.Invoke(processedCount, totalRows);
                        Console.WriteLine($"[BulkImportShifts] Progress: {processedCount}/{totalRows} ({(processedCount * 100.0 / totalRows):F1}%)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BulkImportShifts] ERROR in progress callback at row {processedCount}: {ex.Message}");
                    }
                }
            }

            // Save remaining batches
            if (shiftsToInsert.Any())
            {
                await _dbcontext.RiderShifts.AddRangeAsync(shiftsToInsert);
                await _dbcontext.SaveChangesAsync();
                Console.WriteLine($"[BulkImportShifts] Saved final batch of {shiftsToInsert.Count} new shifts");
            }

            if (shiftsToUpdate.Any())
            {
                await _dbcontext.SaveChangesAsync();
                Console.WriteLine($"[BulkImportShifts] Updated final batch of {shiftsToUpdate.Count} shifts");
            }

            // Final progress update
            try
            {
                progressCallback?.Invoke(totalRows, totalRows);
                Console.WriteLine($"[BulkImportShifts] Final progress callback sent: {totalRows}/{totalRows}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BulkImportShifts] ERROR in final progress callback: {ex.Message}");
            }

            Console.WriteLine($"[BulkImportShifts] Import complete:");
            Console.WriteLine($"  - Total: {totalRows}");
            Console.WriteLine($"  - Successful: {successfulShifts}");
            Console.WriteLine($"  - Updated: {updatedShifts}");
            Console.WriteLine($"  - Skipped Duplicates: {skippedDuplicates}");
            Console.WriteLine($"  - WorkingId Not Found: {workingIdNotFound}");
            Console.WriteLine($"  - Housing Not Found: {housingNotFound}");
            Console.WriteLine($"  - Validation Errors: {validationErrors}");

            var response = new RiderShiftBulkImportResponse(
                TotalRecordsProcessed: totalRows,
                SuccessfulShifts: successfulShifts,
                UpdatedShifts: updatedShifts,
                SkippedDuplicates: skippedDuplicates,
                WorkingIdNotFound: workingIdNotFound,
                HousingNotFound: housingNotFound,
                ValidationErrors: validationErrors,
                Details: details,
                ProcessingErrors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BulkImportShifts] FATAL ERROR: {ex}");
            return Result.Failure<RiderShiftBulkImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    // ============================================
    // HELPER METHODS - ADD TO ImportService.cs
    // ============================================

    private async Task<Dictionary<string, ShiftRiderInfo>> LoadRiderLookupForShifts()
    {
        var lookup = new Dictionary<string, ShiftRiderInfo>(StringComparer.OrdinalIgnoreCase);

        // Load from RiderDetails
        var riderDetails = await _dbcontext.RiderDetails
            .Where(r => !string.IsNullOrEmpty(r.WorkingId))
            .Select(r => new ShiftRiderInfo
            {
                RiderId = r.Id,
                WorkingId = r.WorkingId!,
                IqamaNo = r.EmployeeIqamaNo,
                Source = "RiderDetails"
            })
            .AsNoTracking()
            .ToListAsync();

        foreach (var rider in riderDetails)
        {
            var key = rider.WorkingId.Trim().ToLower();
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = rider;
            }
        }

        // Load from WorkingIdHistory
        var historyRecords = await _dbcontext.RiderWorkingIdHistories
            .Where(h => !string.IsNullOrEmpty(h.WorkingId))
            .Include(h => h.Employee)
                .ThenInclude(e => e.RiderDetails)
            .Where(h => h.Employee.RiderDetails != null)
            .Select(h => new ShiftRiderInfo
            {
                RiderId = h.Employee.RiderDetails!.Id,
                WorkingId = h.WorkingId,
                IqamaNo = h.RiderIqamaNo,
                Source = "WorkingIdHistory"
            })
            .AsNoTracking()
            .ToListAsync();

        foreach (var record in historyRecords)
        {
            var key = record.WorkingId.Trim().ToLower();
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = record;
            }
        }

        return lookup;
    }

    private async Task<Dictionary<int, string>> LoadHousingLookup()
    {
        return await _dbcontext.Housings
            .AsNoTracking()
            .ToDictionaryAsync(h => h.Id, h => h.Name);
    }

    private IXLRow? FindShiftImportHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
        "driverId", "Driver ID", "Working ID", "WorkingID", "معرف السائق", "رقم السائق",
        "shiftDate", "Shift Date", "Date", "التاريخ", "تاريخ الوردية",
        "acceptedOrders", "Accepted Orders", "Orders", "الطلبات المقبولة",
        "housingId", "Housing ID", "Housing", "رقم السكن"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cellValues = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int matchCount = cellValues.Count(cv =>
                knownColumns.Any(kc =>
                    cv.Equals(kc, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(kc.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));

            if (matchCount >= 2)
                return row;
        }

        return worksheet.Row(1);
    }

    private ShiftImportColumnMapping BuildShiftImportColumnMapping(IXLRow headerRow)
    {
        var mapping = new ShiftImportColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.WorkingIdCol = FindColumn(cells,
            "driverId", "Driver ID", "Working ID", "WorkingID", "معرف السائق", "رقم السائق", "معرف العمل");

        mapping.ShiftDateCol = FindColumn(cells,
            "reqDate", "Shift Date", "Date", "التاريخ", "تاريخ الوردية", "تاريخ");

        mapping.AcceptedOrdersCol = FindColumn(cells,
            "dailyRec", "Accepted Orders", "Orders", "Daily Orders", "الطلبات المقبولة", "الطلبات");

        mapping.HousingIdCol = FindColumn(cells,
            "housing", "Housing ID", "Housing", "رقم السكن", "السكن");

        var missing = new List<string>();
        if (mapping.WorkingIdCol == 0) missing.Add("WorkingId/DriverId");
        if (mapping.ShiftDateCol == 0) missing.Add("ShiftDate");
        if (mapping.AcceptedOrdersCol == 0) missing.Add("AcceptedOrders");

        if (missing.Any())
        {
            mapping.IsValid = false;
            mapping.ErrorMessage = $"Required columns missing: {string.Join(", ", missing)}";
        }
        else
        {
            mapping.IsValid = true;
        }

        return mapping;
    }

    private DateOnly? ParseShiftDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Clean up the input
        dateStr = dateStr.Trim();

        // Try direct DateOnly parse first
        if (DateOnly.TryParse(dateStr, out DateOnly directResult))
        {
            return directResult;
        }

        // Comprehensive list of date formats
        string[] formats = {
        // Excel common formats
        "M/d/yyyy", "MM/dd/yyyy", "M/dd/yyyy", "MM/d/yyyy",
        "d/M/yyyy", "dd/MM/yyyy", "d/MM/yyyy", "dd/M/yyyy",
        
        // ISO formats
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
        "yyyy-M-d", "yyyy/M/d",
        
        // With dashes
        "dd-MM-yyyy", "d-M-yyyy", "MM-dd-yyyy", "M-d-yyyy",
        
        // With dots (European)
        "dd.MM.yyyy", "d.M.yyyy",
        
        // Month names
        "dd-MMM-yyyy", "d-MMM-yyyy", "MMM dd yyyy",
        "dd MMM yyyy", "d MMM yyyy",
        
        // Two digit years
        "dd/MM/yy", "d/M/yy", "MM/dd/yy", "M/d/yy",
        "yy-MM-dd", "yy/MM/dd",
        
        // With time (ignore time part)
        "M/d/yyyy h:mm", "M/d/yyyy HH:mm",
        "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss"
    };

        // Try parsing with each format
        foreach (var format in formats)
        {
            // Try with invariant culture
            if (DateTime.TryParseExact(dateStr, format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date))
            {
                return DateOnly.FromDateTime(date);
            }
        }

        // Try general parse
        if (DateTime.TryParse(dateStr, out DateTime generalDate))
        {
            return DateOnly.FromDateTime(generalDate);
        }

        return null;
    }

    private ShiftImportRowData ParseShiftImportRowData(
      IXLRow row,
      ShiftImportColumnMapping map,
      int rowNumber)
    {
        var data = new ShiftImportRowData { RowNumber = rowNumber };

        try
        {
            data.WorkingId = GetCellValue(row, map.WorkingIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.WorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "WorkingId/DriverId is required";
                return data;
            }

            var dateStr = GetCellValue(row, map.ShiftDateCol);
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "ShiftDate is required";
                return data;
            }

            var shiftDate = ParseShiftDate(dateStr);
            if (!shiftDate.HasValue)
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid date: '{dateStr}'";
                return data;
            }
            data.ShiftDate = shiftDate.Value;

            // ✅ FIXED: Parse AcceptedOrders
            var ordersStr = GetCellValue(row, map.AcceptedOrdersCol);

            if (string.IsNullOrWhiteSpace(ordersStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "AcceptedOrders is required";
                return data;
            }

            if (!TryParseInt(ordersStr, out int acceptedOrders))
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid AcceptedOrders: '{ordersStr}'";
                return data;
            }

            if (acceptedOrders < 0)
            {
                data.IsValid = false;
                data.ErrorMessage = $"AcceptedOrders cannot be negative: {acceptedOrders}";
                return data;
            }

            data.AcceptedOrders = acceptedOrders;
            data.ShiftStatus = acceptedOrders >= 14 ? "completed" : "failed";

            var housingStr = GetCellValue(row, map.HousingIdCol);
            if (!string.IsNullOrWhiteSpace(housingStr) && TryParseInt(housingStr, out int housingId))
            {
                data.HousingId = housingId;
            }

            data.IsValid = true;
        }
        catch (Exception ex)
        {
            data.IsValid = false;
            data.ErrorMessage = $"Error parsing row: {ex.Message}";
        }

        return data;
    }

    // ============================================
    // INTERNAL CLASSES - ADD TO ImportService.cs
    // ============================================

    internal class ShiftRiderInfo
    {
        public int RiderId { get; set; }
        public string WorkingId { get; set; } = string.Empty;
        public long IqamaNo { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    internal class ShiftImportColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int WorkingIdCol { get; set; }
        public int ShiftDateCol { get; set; }
        public int AcceptedOrdersCol { get; set; }
        public int HousingIdCol { get; set; }
    }

    internal class ShiftImportRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? WorkingId { get; set; }
        public DateOnly? ShiftDate { get; set; }
        public int AcceptedOrders { get; set; }
        public int? HousingId { get; set; }
        public string? ShiftStatus { get; set; }
    }

    internal class HousingColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int IqamaNoCol { get; set; }
        public int HousingNameCol { get; set; }
    }

    internal class HousingRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? IqamaNo { get; set; }
        public string? HousingName { get; set; }
    }
    internal class VehicleColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public int VehicleNumberCol { get; set; }
        public int SerialNumberCol { get; set; }
        public int PlateNumberACol { get; set; }
        public int PlateNumberECol { get; set; }

        public int VehicleTypeCol { get; set; }
        public int ManufacturerCol { get; set; }
        public int ManufactureYearCol { get; set; }
        public int LicenseExpiryDateCol { get; set; }
        public int LocationCol { get; set; }
        public int StatusCol { get; set; }
        public int RiderIqamaNoCol { get; set; }
    }

    internal class VehicleRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public string? VehicleNumber { get; set; }
        public int SerialNumber { get; set; }
        public string? PlateNumberA { get; set; }
        public string? PlateNumberE { get; set; }

        public string? VehicleType { get; set; }
        public string? Manufacturer { get; set; }
        public int ManufactureYear { get; set; }
        public DateOnly? LicenseExpiryDate { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public long? RiderIqamaNo { get; set; }

        public string? OwnerName { get; set; }
        public long OwnerId { get; set; }

    }

    internal class ColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public int IqamaNoCol { get; set; }
        public int NameARCol { get; set; }
        public int NameENCol { get; set; }
        public int IqamaEndMCol { get; set; }
        public int IqamaEndHCol { get; set; }
        public int PassportNoCol { get; set; }
        public int PassportEndCol { get; set; }
        public int SponsorCol { get; set; }
        public int SponsorNoCol { get; set; }
        public int JobTitleCol { get; set; }
        public int CountryCol { get; set; }
        public int PhoneCol { get; set; }
        public int DateOfBirthCol { get; set; }
        public int StatusCol { get; set; }
        public int IBANCol { get; set; }
        public int INKSACol { get; set; }

        public int WorkingIdCol { get; set; }
        public int TshirtSizeCol { get; set; }
        public int LicenseNumberCol { get; set; }
        public int CompanyNameCol { get; set; }
    }

    internal class RowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public long IqamaNo { get; set; }
        public string? NameAR { get; set; }
        public string? NameEN { get; set; }
        public DateOnly? IqamaEndM { get; set; }
        public DateOnly? IqamaEndH { get; set; }
        public string? PassportNo { get; set; }
        public DateOnly? PassportEnd { get; set; }
        public string? Sponsor { get; set; }
        public long SponsorNo { get; set; }
        public string? JobTitle { get; set; }
        public string? Country { get; set; }
        public string? Phone { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Status { get; set; }
        public string? IBAN { get; set; }
        public bool INKSA { get; set; }

        public string? WorkingId { get; set; }
        public string? TshirtSize { get; set; }
        public string? LicenseNumber { get; set; }
        public string? CompanyName { get; set; }
        public int? CompanyId { get; set; }
    }

    #endregion
}

