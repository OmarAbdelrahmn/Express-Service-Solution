using Application.Abstraction;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Application.Service.Freelancer;

public class FreelancerService(ApplicationDbcontext dbcontext) : IFreelancerService
{
    public async Task<Result<KetaFreelancerImportResponse>> ImportKetaFreelancersFromExcelAsync(
        IFormFile file,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<KetaFreelancerImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<KetaFreelancerImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<KetaFreelancerImportRowResult>();
        var errors = new List<string>();
        int successfulImports = 0;
        int failedRecords = 0;

        try
        {
            Console.WriteLine($"[KetaFreelancer] Starting import for file: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
            {
                Console.WriteLine("[KetaFreelancer] ERROR: Could not read worksheet");
                return Result.Failure<KetaFreelancerImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));
            }

            Console.WriteLine($"[KetaFreelancer] Worksheet loaded: {worksheet.Name}");

            var headerRow = FindKetaFreelancerHeaderRow(worksheet);
            if (headerRow == null)
            {
                Console.WriteLine("[KetaFreelancer] ERROR: No header row found");
                return Result.Failure<KetaFreelancerImportResponse>(
                    new Error("EmptyFile", "Excel file has no header row", 400));
            }

            Console.WriteLine($"[KetaFreelancer] Header row found at row {headerRow.RowNumber()}");

            var columnMap = BuildKetaFreelancerColumnMapping(headerRow);
            if (!columnMap.IsValid)
            {
                Console.WriteLine($"[KetaFreelancer] ERROR: Invalid columns - {columnMap.ErrorMessage}");
                return Result.Failure<KetaFreelancerImportResponse>(
                    new Error("InvalidColumns", columnMap.ErrorMessage!, 400));
            }

            Console.WriteLine("[KetaFreelancer] Column mapping successful");

            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            var totalRows = dataRows.Count;
            Console.WriteLine($"[KetaFreelancer] Total data rows to process: {totalRows}");

            if (totalRows == 0)
            {
                Console.WriteLine("[KetaFreelancer] WARNING: No data rows found");
                return Result.Failure<KetaFreelancerImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));
            }

            // ── Pass 1: Count orders per WorkingId + Month ────────────────────────
            var freelancerData = new Dictionary<string, KetaFreelancerRowData>();
            var rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;

                try
                {
                    var rowData = ParseKetaFreelancerRowData(row, columnMap, rowNumber);

                    if (!rowData.IsValid)
                    {
                        failedRecords++;
                        results.Add(new KetaFreelancerImportRowResult(
                            rowNumber, false,
                            "N/A", "N/A", "N/A",
                            0, "N/A", "N/A", 0,
                            false, false, [],
                            $"Parse error: {rowData.ErrorMessage}"
                        ));
                        continue;
                    }

                    var key = $"{rowData.WorkingId}_{rowData.Month}";

                    if (!freelancerData.ContainsKey(key))
                    {
                        freelancerData[key] = new KetaFreelancerRowData
                        {
                            WorkingId = rowData.WorkingId,
                            Month = rowData.Month,
                            TotalOrders = 1,
                            IsValid = true
                        };
                    }
                    else
                    {
                        freelancerData[key].TotalOrders++;
                    }
                }
                catch (Exception ex)
                {
                    failedRecords++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");

                    results.Add(new KetaFreelancerImportRowResult(
                        rowNumber, false,
                        "N/A", "N/A", "N/A",
                        0, "N/A", "N/A", 0,
                        false, false, [],
                        $"Exception: {ex.Message}"
                    ));
                }
            }

            Console.WriteLine($"[KetaFreelancer] Unique WorkingId+Month combinations: {freelancerData.Count}");

            // ── Pass 2: Resolve each WorkingId → RiderId, merge same rider+month ──
            var resolvedData = new Dictionary<string, KetaFreelancerResolvedData>();

            foreach (var kvp in freelancerData)
            {
                var data = kvp.Value;

                // 1) Try current RiderDetails
                var rider = await dbcontext.RiderDetails
                    .Include(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                    .FirstOrDefaultAsync(r => r.WorkingId == data.WorkingId, cancellationToken);

                // 2) Fallback: most recent RiderWorkingIdHistory entry for this WorkingId
                if (rider == null)
                {
                    Console.WriteLine($"[KetaFreelancer] '{data.WorkingId}' not in RiderDetails, checking history...");

                    var history = await dbcontext.RiderWorkingIdHistories
                        .Where(h => h.WorkingId == data.WorkingId)
                        .OrderByDescending(h => h.StartDate)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (history != null)
                    {
                        rider = await dbcontext.RiderDetails
                            .Include(r => r.Employee)
                                .ThenInclude(e => e.Housing)
                            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == history.RiderIqamaNo, cancellationToken);

                        if (rider != null)
                            Console.WriteLine($"[KetaFreelancer] Resolved '{data.WorkingId}' via history → RiderId {rider.Id}");
                    }
                }

                // 3) Still not found → mark unresolved
                if (rider == null)
                {
                    Console.WriteLine($"[KetaFreelancer] Could not resolve WorkingId '{data.WorkingId}'");

                    var unresolvedKey = $"UNRESOLVED_{data.WorkingId}_{data.Month}";
                    resolvedData[unresolvedKey] = new KetaFreelancerResolvedData
                    {
                        Month = data.Month!,
                        TotalOrders = data.TotalOrders,
                        WorkingIds = [data.WorkingId!],
                        IsUnresolved = true,
                        UnresolvedWorkingId = data.WorkingId
                    };
                    continue;
                }

                // 4) Resolved → merge into RiderId+Month bucket (handles two WorkingIds same rider)
                var resolvedKey = $"{rider.Id}_{data.Month}";

                if (!resolvedData.ContainsKey(resolvedKey))
                {
                    resolvedData[resolvedKey] = new KetaFreelancerResolvedData
                    {
                        RiderId = rider.Id,
                        Rider = rider,
                        Month = data.Month!,
                        TotalOrders = data.TotalOrders,
                        WorkingIds = [data.WorkingId!],
                        IsUnresolved = false
                    };
                }
                else
                {
                    // Same rider appeared under a different WorkingId in the same month → sum orders
                    resolvedData[resolvedKey].TotalOrders += data.TotalOrders;
                    resolvedData[resolvedKey].WorkingIds.Add(data.WorkingId!);
                    Console.WriteLine(
                        $"[KetaFreelancer] Merged WorkingId '{data.WorkingId}' into RiderId {rider.Id} " +
                        $"for {data.Month} → new total: {resolvedData[resolvedKey].TotalOrders}");
                }
            }

            Console.WriteLine($"[KetaFreelancer] Resolved buckets (unique rider+month): {resolvedData.Count}");

            // ── Pass 3: Persist each resolved bucket ─────────────────────────────
            int processedRow = headerRow.RowNumber() + 1;

            foreach (var kvp in resolvedData)
            {
                using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    var data = kvp.Value;
                    var warnings = new List<string>();

                    // Handle unresolved WorkingIds
                    if (data.IsUnresolved)
                    {
                        failedRecords++;
                        errors.Add($"WorkingId '{data.UnresolvedWorkingId}' not found in RiderDetails or history");

                        results.Add(new KetaFreelancerImportRowResult(
                            processedRow, false,
                            data.UnresolvedWorkingId!, "N/A", "N/A",
                            null, null,
                            data.Month, data.TotalOrders,
                            false, false, [],
                            $"Rider with WorkingId '{data.UnresolvedWorkingId}' not found in RiderDetails or WorkingId history"
                        ));

                        await transaction.RollbackAsync(cancellationToken);
                        processedRow++;
                        continue;
                    }

                    var rider = data.Rider;

                    // Warn when orders were merged from multiple WorkingIds
                    if (data.WorkingIds.Count > 1)
                        warnings.Add(
                            $"Orders merged from multiple WorkingIds: {string.Join(", ", data.WorkingIds)} " +
                            $"→ summed total: {data.TotalOrders}");

                    // Store the most recently merged WorkingId as the primary one
                    var primaryWorkingId = data.WorkingIds.Last();

                    // Check if a record already exists for this rider + month
                    var existing = await dbcontext.KetaFreeLancers
                        .FirstOrDefaultAsync(k =>
                            k.RiderId == rider.Id &&
                            k.Month == data.Month,
                            cancellationToken);

                    bool created = false;
                    bool updated = false;

                    if (existing == null)
                    {
                        var ketaFreelancer = new KetaFreeLancer
                        {
                            RiderId = rider.Id,
                            WorkingId = primaryWorkingId,
                            Month = data.Month,
                            TotalOrders = data.TotalOrders,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        await dbcontext.KetaFreeLancers.AddAsync(ketaFreelancer, cancellationToken);
                        created = true;
                        successfulImports++;
                    }
                    else
                    {
                        var oldTotalOrders = existing.TotalOrders;
                        existing.TotalOrders = data.TotalOrders;
                        existing.WorkingId = primaryWorkingId;

                        updated = true;
                        successfulImports++;
                        warnings.Add($"Updated existing record — old total: {oldTotalOrders}, new total: {data.TotalOrders}");
                    }

                    await dbcontext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    results.Add(new KetaFreelancerImportRowResult(
                        processedRow, true,
                        string.Join(" + ", data.WorkingIds),
                        rider.Employee.NameEN,
                        rider.Employee.NameAR,
                        rider.Employee.IqamaNo,
                        rider.Employee.Housing?.Name,
                        data.Month,
                        data.TotalOrders,
                        created, updated,
                        warnings,
                        null
                    ));

                    Console.WriteLine(
                        $"[KetaFreelancer] ✓ RiderId {rider.Id} ({rider.Employee.NameEN}) | " +
                        $"Month: {data.Month} | WorkingIds: {string.Join("+", data.WorkingIds)} | " +
                        $"Orders: {data.TotalOrders}");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    failedRecords++;
                    errors.Add($"RiderId {kvp.Value.RiderId}, Month {kvp.Value.Month}: {ex.Message}");

                    results.Add(new KetaFreelancerImportRowResult(
                        processedRow, false,
                        string.Join("+", kvp.Value.WorkingIds),
                        "N/A", "N/A", null, null,
                        kvp.Value.Month, kvp.Value.TotalOrders,
                        false, false, [],
                        $"Exception: {ex.Message}"
                    ));
                }

                processedRow++;
            }

            Console.WriteLine($"[KetaFreelancer] Import complete:");
            Console.WriteLine($"  - Total Excel Rows : {totalRows}");
            Console.WriteLine($"  - Unique Records   : {resolvedData.Count}");
            Console.WriteLine($"  - Successful       : {successfulImports}");
            Console.WriteLine($"  - Failed           : {failedRecords}");

            var response = new KetaFreelancerImportResponse(
                totalRows,
                successfulImports,
                failedRecords,
                results,
                errors,
                DateTime.UtcNow.AddHours(3)
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KetaFreelancer] FATAL ERROR: {ex}");
            return Result.Failure<KetaFreelancerImportResponse>(
                new Error("ProcessingError", $"Failed to process Excel file: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<KetaFreelancerResponse>>> GetKetaFreelancersByMonthAsync(
        string month,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsValidMonthFormat(month))
            {
                return Result.Failure<IEnumerable<KetaFreelancerResponse>>(
                    new Error("InvalidMonth", "Month must be in format yyyy-MM (e.g., 2025-12)", 400));
            }

            Console.WriteLine($"[KetaFreelancer] Retrieving records for month: {month}");

            var freelancers = await dbcontext.KetaFreeLancers
                .Include(k => k.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Where(k => k.Month == month)
                .OrderBy(k => k.WorkingId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!freelancers.Any())
            {
                return Result.Failure<IEnumerable<KetaFreelancerResponse>>(
                    new Error("NotFound", $"No records found for month {month}", 404));
            }

            var responses = freelancers.Select(k => new KetaFreelancerResponse(
                k.Id,
                k.RiderId,
                k.WorkingId,
                k.Rider.Employee.NameEN,
                k.Rider.Employee.NameAR,
                k.Rider.Employee.IqamaNo,
                k.Rider.Employee.Housing?.Name,
                k.Month,
                k.TotalOrders,
                k.CreatedAt
            ));

            Console.WriteLine($"[KetaFreelancer] Found {freelancers.Count} records for {month}");

            return Result.Success(responses);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KetaFreelancer] ERROR retrieving records: {ex}");
            return Result.Failure<IEnumerable<KetaFreelancerResponse>>(
                new Error("ServerError", $"Error retrieving records: {ex.Message}", 500));
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    private static IXLRow? FindKetaFreelancerHeaderRow(IXLWorksheet worksheet)
    {
        var knownColumns = new[]
        {
            "WorkingId", "Working ID", "معرف العمل", "رقم السائق",
            "Month", "الشهر", "التاريخ"
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

    private static KetaFreelancerColumnMapping BuildKetaFreelancerColumnMapping(IXLRow headerRow)
    {
        var mapping = new KetaFreelancerColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        mapping.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working ID", "معرف العمل", "رقم السائق", "Rider ID");

        mapping.MonthCol = FindColumn(cells,
            "Month", "الشهر", "التاريخ", "Date", "Month/Year");

        var missing = new List<string>();
        if (mapping.WorkingIdCol == 0) missing.Add("WorkingId");
        if (mapping.MonthCol == 0) missing.Add("Month");

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

    private static int FindColumn(List<IXLCell> cells, params string[] possibleNames)
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
                    if (headerValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;

                string headerNoSpaces = headerValue.Replace(" ", "");
                foreach (var name in possibleNames)
                {
                    string nameNoSpaces = name.Replace(" ", "");
                    if (headerNoSpaces.Equals(nameNoSpaces, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
                }

                foreach (var name in possibleNames)
                    if (headerValue.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return cell.Address.ColumnNumber;
            }
            catch { }
        }

        return 0;
    }

    private static KetaFreelancerRowData ParseKetaFreelancerRowData(
        IXLRow row,
        KetaFreelancerColumnMapping map,
        int rowNumber)
    {
        var data = new KetaFreelancerRowData { RowNumber = rowNumber };

        try
        {
            data.WorkingId = GetCellValue(row, map.WorkingIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(data.WorkingId))
            {
                data.IsValid = false;
                data.ErrorMessage = "WorkingId is required";
                return data;
            }

            var monthStr = GetCellValue(row, map.MonthCol)?.Trim();
            if (string.IsNullOrWhiteSpace(monthStr))
            {
                data.IsValid = false;
                data.ErrorMessage = "Month is required";
                return data;
            }

            data.Month = NormalizeMonthFormat(monthStr);
            if (string.IsNullOrWhiteSpace(data.Month))
            {
                data.IsValid = false;
                data.ErrorMessage = $"Invalid month format: '{monthStr}'. Expected format: yyyy-MM (e.g., 2025-12)";
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

    private static string? GetCellValue(IXLRow row, int columnIndex)
    {
        if (columnIndex == 0) return null;

        try
        {
            var cell = row.Cell(columnIndex);
            if (cell.IsEmpty()) return null;

            if (cell.IsMerged())
                cell = cell.MergedRange().FirstCell();

            if (cell.DataType == XLDataType.Number)
            {
                var numValue = cell.GetDouble();
                return numValue == Math.Floor(numValue)
                    ? ((long)numValue).ToString()
                    : numValue.ToString();
            }

            if (cell.DataType == XLDataType.DateTime)
            {
                try { return cell.GetDateTime().ToString("yyyy-MM"); }
                catch { return cell.GetText().Trim(); }
            }

            if (cell.DataType == XLDataType.Text)
                return cell.GetText().Trim();

            return cell.Value.ToString()?.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetCellValue] Error at column {columnIndex}: {ex.Message}");
            return null;
        }
    }

    private static string? NormalizeMonthFormat(string monthStr)
    {
        if (string.IsNullOrWhiteSpace(monthStr))
            return null;

        monthStr = monthStr.Trim();

        if (System.Text.RegularExpressions.Regex.IsMatch(monthStr, @"^\d{4}-\d{2}$"))
            return monthStr;

        string[] formats =
        {
            "yyyy-MM", "yyyy/MM", "MM/yyyy", "MM-yyyy",
            "MMM yyyy", "MMMM yyyy",
            "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy"
        };

        foreach (var format in formats)
            if (DateTime.TryParseExact(monthStr, format,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                return date.ToString("yyyy-MM");

        if (DateTime.TryParse(monthStr, out DateTime generalDate))
            return generalDate.ToString("yyyy-MM");

        return null;
    }

    private static bool IsValidMonthFormat(string month)
    {
        if (string.IsNullOrWhiteSpace(month))
            return false;

        return System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$");
    }

    // ============================================
    // INTERNAL CLASSES
    // ============================================

    internal class KetaFreelancerColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int WorkingIdCol { get; set; }
        public int MonthCol { get; set; }
    }

    internal class KetaFreelancerRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? WorkingId { get; set; }
        public string? Month { get; set; }
        public int TotalOrders { get; set; }
    }

    internal class KetaFreelancerResolvedData
    {
        public int RiderId { get; set; }
        public RiderDetails Rider { get; set; } = default!;
        public string Month { get; set; } = string.Empty;
        public int TotalOrders { get; set; }
        public List<string> WorkingIds { get; set; } = [];
        public bool IsUnresolved { get; set; }
        public string? UnresolvedWorkingId { get; set; }
    }
}