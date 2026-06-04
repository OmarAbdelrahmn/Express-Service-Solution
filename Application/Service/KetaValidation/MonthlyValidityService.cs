using Application.Abstraction;
using Application.Service.KetaValidation;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Keeta;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static Application.Service.KetaValidation.IMonthlyValidityService;

namespace Application.Service.MonthlyValidity;

public class MonthlyValidityService(ApplicationDbcontext db) : IMonthlyValidityService
{
    private readonly ApplicationDbcontext _db = db;

    private static readonly Dictionary<int, string> MonthNames = new()
    {
        { 1,  "يناير"  }, { 2,  "فبراير" }, { 3,  "مارس"   },
        { 4,  "أبريل"  }, { 5,  "مايو"   }, { 6,  "يونيو"  },
        { 7,  "يوليو"  }, { 8,  "أغسطس"  }, { 9,  "سبتمبر" },
        { 10, "أكتوبر" }, { 11, "نوفمبر" }, { 12, "ديسمبر" }
    };


    // ============================================================
    // ADD TO: Application/Service/Import/ImportService.cs
    // Place inside the ImportService class, inside a new region
    // ============================================================

    #region Keeta Driver Shift Import

    public async Task<Result<KeetaShiftImportResponse>> ImportKeetaDriverShiftsAsync(
        IFormFile file,
        string uploadedBy,
        Action<int, int>? progressCallback = null)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<KeetaShiftImportResponse>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<KeetaShiftImportResponse>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        var results = new List<KeetaShiftRowResult>();
        var errors = new List<string>();

        int driversFound = 0;
        int driversNotFound = 0;
        int shiftsCreated = 0;
        int shiftsUpdated = 0;
        int notInShift = 0;
        int noQualifiedSlots = 0;
        int errorRows = 0;
        DateOnly? earliestDate = null;
        DateOnly? latestDate = null;

        try
        {
            Console.WriteLine($"[KeetaShiftImport] Starting: {file.FileName}");

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet == null)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("InvalidWorksheet", "Could not read worksheet", 400));

            // ── Header row ─────────────────────────────────────────────────────
            var headerRow = FindKeetaShiftHeaderRow(worksheet);
            if (headerRow == null)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("EmptyFile", "No header row found", 400));

            Console.WriteLine($"[KeetaShiftImport] Header at row {headerRow.RowNumber()}");

            var colMap = BuildKeetaShiftColumnMapping(headerRow);
            if (!colMap.IsValid)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("InvalidColumns", colMap.ErrorMessage!, 400));

            Console.WriteLine("[KeetaShiftImport] Column mapping OK — " +
                $"Date={colMap.DateCol} DriverId={colMap.DriverIdCol} " +
                $"Summary={colMap.ShiftSummaryCol} InShift={colMap.IsInShiftCol} " +
                $"ConnTime={colMap.ConnectionTimeCol} Tasks={colMap.TasksDeliveredCol}");

            // ── Rider lookup (WorkingId → RiderId) ─────────────────────────────
            // Primary: RiderDetails.WorkingId
            var riderLookup = new Dictionary<string, (int Id, string WorkingId)>(
                StringComparer.OrdinalIgnoreCase);

            var riders = await _db.RiderDetails
                .Where(r => !string.IsNullOrEmpty(r.WorkingId))
                .Select(r => new { r.Id, r.WorkingId })
                .AsNoTracking()
                .ToListAsync();

            foreach (var r in riders)
                riderLookup[r.WorkingId!.Trim()] = (r.Id, r.WorkingId!);

            // Fallback: WorkingIdHistory (catches transferred / renamed riders)
            var history = await _db.RiderWorkingIdHistories
                .Where(h => !string.IsNullOrEmpty(h.WorkingId))
                .Include(h => h.Employee)
                    .ThenInclude(e => e.RiderDetails)
                .Where(h => h.Employee.RiderDetails != null)
                .Select(h => new { RiderId = h.Employee.RiderDetails!.Id, h.WorkingId })
                .AsNoTracking()
                .ToListAsync();

            foreach (var h in history)
            {
                var k = h.WorkingId.Trim();
                if (!riderLookup.ContainsKey(k))
                    riderLookup[k] = (h.RiderId, h.WorkingId);
            }

            Console.WriteLine($"[KeetaShiftImport] Rider lookup loaded: {riderLookup.Count} entries");

            // ── Data rows ───────────────────────────────────────────────────────
            var dataRows = worksheet.RowsUsed()
                .Where(r => r.RowNumber() > headerRow.RowNumber())
                .ToList();

            int totalRows = dataRows.Count;
            if (totalRows == 0)
                return Result.Failure<KeetaShiftImportResponse>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            Console.WriteLine($"[KeetaShiftImport] Data rows: {totalRows}");

            progressCallback?.Invoke(0, totalRows);

            int processed = 0;
            int rowNumber = headerRow.RowNumber();

            foreach (var row in dataRows)
            {
                rowNumber++;
                processed++;

                try
                {
                    // ── Parse row ───────────────────────────────────────────────
                    var rd = ParseKeetaShiftRow(row, colMap, rowNumber);

                    if (!rd.IsValid)
                    {
                        errorRows++;
                        errors.Add($"Row {rowNumber}: {rd.ErrorMessage}");
                        results.Add(new KeetaShiftRowResult(
                            rowNumber, rd.PlatformDriverId ?? "N/A",
                            rd.ReportDate ?? DateOnly.MinValue,
                            false, null, null, false, 0, 0, 0,
                            [], KeetaImportAction.Error, rd.ErrorMessage));
                        continue;
                    }

                    // ── Date range tracking ─────────────────────────────────────
                    if (!earliestDate.HasValue || rd.ReportDate!.Value < earliestDate.Value)
                        earliestDate = rd.ReportDate;
                    if (!latestDate.HasValue || rd.ReportDate!.Value > latestDate.Value)
                        latestDate = rd.ReportDate;

                    // ── Rider resolution ────────────────────────────────────────
                    // Try the explicit WorkingId column first; fall back to PlatformDriverId.
                    var lookupKey = !string.IsNullOrWhiteSpace(rd.WorkingId)
                        ? rd.WorkingId!.Trim()
                        : rd.PlatformDriverId!.Trim();

                    int? riderId = null;
                    string? resolvedWorkingId = null;
                    KeetaImportAction action;

                    if (riderLookup.TryGetValue(lookupKey, out var riderInfo))
                    {
                        riderId = riderInfo.Id;
                        resolvedWorkingId = riderInfo.WorkingId;
                        driversFound++;
                    }
                    else
                    {
                        driversNotFound++;
                    }

                    // ── Not-in-shift rows ───────────────────────────────────────
                    if (!rd.IsInShift)
                    {
                        notInShift++;
                        action = KeetaImportAction.NotInShift;
                        // Still upsert so supervisors / zero-time data is recorded.
                    }

                    // ── Parse qualified slots (top-3 by duration) ───────────────
                    var qualifiedSlots = ParseQualifiedKeetaSlots(rd.RawShiftSummary);

                    if (rd.IsInShift && qualifiedSlots.Count == 0)
                        noQualifiedSlots++;

                    // ── Upsert KeetaDriverShift ─────────────────────────────────
                    var existing = await _db.KeetaDriverShifts
                        .Include(k => k.ShiftSlots)
                        .FirstOrDefaultAsync(k =>
                            k.PlatformDriverId == rd.PlatformDriverId &&
                            k.ReportDate == rd.ReportDate!.Value);

                    if (existing == null)
                    {
                        var newShift = new KeetaDriverShift
                        {
                            ReportDate = rd.ReportDate!.Value,
                            PlatformDriverId = rd.PlatformDriverId!,
                            WorkingId = resolvedWorkingId,
                            RiderId = riderId,
                            Supervisor = rd.Supervisor,
                            IsInShift = rd.IsInShift,
                            TotalConnectionTimeRaw = rd.ConnectionTimeRaw,
                            TotalConnectionMinutes = rd.ConnectionMinutes,
                            TasksDelivered = rd.TasksDelivered,
                            RawShiftSummary = rd.RawShiftSummary,
                            QualifiedSlotsCount = qualifiedSlots.Count,
                            ImportedBy = uploadedBy,
                            CreatedAt = DateTime.UtcNow.AddHours(3)
                        };

                        foreach (var s in qualifiedSlots)
                            newShift.ShiftSlots.Add(MapSlot(s));

                        await _db.KeetaDriverShifts.AddAsync(newShift);
                        shiftsCreated++;
                        action = riderId.HasValue
                            ? KeetaImportAction.Created
                            : (rd.IsInShift ? KeetaImportAction.DriverNotFound : KeetaImportAction.NotInShift);
                    }
                    else
                    {
                        // Update all scalar fields; replace slots completely.
                        existing.WorkingId = resolvedWorkingId ?? existing.WorkingId;
                        existing.RiderId = riderId ?? existing.RiderId;
                        existing.Supervisor = rd.Supervisor;
                        existing.IsInShift = rd.IsInShift;
                        existing.TotalConnectionTimeRaw = rd.ConnectionTimeRaw;
                        existing.TotalConnectionMinutes = rd.ConnectionMinutes;
                        existing.TasksDelivered = rd.TasksDelivered;
                        existing.RawShiftSummary = rd.RawShiftSummary;
                        existing.QualifiedSlotsCount = qualifiedSlots.Count;
                        existing.UpdatedAt = DateTime.UtcNow.AddHours(3);

                        // Delete and re-create slots (simplest safe strategy).
                        _db.KeetaShiftSlots.RemoveRange(existing.ShiftSlots);
                        existing.ShiftSlots.Clear();

                        foreach (var s in qualifiedSlots)
                            existing.ShiftSlots.Add(MapSlot(s));

                        shiftsUpdated++;
                        action = riderId.HasValue
                            ? KeetaImportAction.Updated
                            : (rd.IsInShift ? KeetaImportAction.DriverNotFound : KeetaImportAction.NotInShift);
                    }

                    await _db.SaveChangesAsync();

                    results.Add(new KeetaShiftRowResult(
                        rowNumber,
                        rd.PlatformDriverId!,
                        rd.ReportDate!.Value,
                        riderId.HasValue,
                        resolvedWorkingId,
                        riderId,
                        rd.IsInShift,
                        rd.TasksDelivered,
                        rd.ConnectionMinutes,
                        qualifiedSlots.Count,
                        qualifiedSlots.Select(s => new KeetaSlotDetail(
                            s.SlotKey, s.DurationRaw, s.DurationMinutes, s.SlotOrder)).ToList(),
                        action,
                        null));

                    Console.WriteLine(
                        $"[KeetaShiftImport] ✓ Row {rowNumber} | Driver={rd.PlatformDriverId} | " +
                        $"Date={rd.ReportDate} | QSlots={qualifiedSlots.Count} | {action}");
                }
                catch (Exception ex)
                {
                    errorRows++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");
                    results.Add(new KeetaShiftRowResult(
                        rowNumber, "N/A", DateOnly.MinValue,
                        false, null, null, false, 0, 0, 0,
                        [], KeetaImportAction.Error, $"Exception: {ex.Message}"));
                    Console.WriteLine($"[KeetaShiftImport] ERROR Row {rowNumber}: {ex.Message}");
                }

                if (processed % 50 == 0)
                {
                    try { progressCallback?.Invoke(processed, totalRows); } catch { }
                    Console.WriteLine($"[KeetaShiftImport] Progress: {processed}/{totalRows}");
                }
            }

            try { progressCallback?.Invoke(totalRows, totalRows); } catch { }

            Console.WriteLine(
                $"[KeetaShiftImport] Done — Created={shiftsCreated} Updated={shiftsUpdated} " +
                $"NotFound={driversNotFound} NotInShift={notInShift} " +
                $"NoSlots={noQualifiedSlots} Errors={errorRows}");

            return Result.Success(new KeetaShiftImportResponse(
                TotalRowsInExcel: totalRows,
                EarliestDate: earliestDate,
                LatestDate: latestDate,
                DriversFound: driversFound,
                DriversNotFound: driversNotFound,
                ShiftsCreated: shiftsCreated,
                ShiftsUpdated: shiftsUpdated,
                NotInShift: notInShift,
                NoQualifiedSlots: noQualifiedSlots,
                ErrorRows: errorRows,
                Results: results,
                ProcessingErrors: errors,
                ProcessedAt: DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KeetaShiftImport] FATAL: {ex}");
            return Result.Failure<KeetaShiftImportResponse>(
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ════════════════════════════════════════════════════════════════════════

    // ── Header row finder ─────────────────────────────────────────────────────

    private IXLRow? FindKeetaShiftHeaderRow(IXLWorksheet worksheet)
    {
        // We recognise the header by finding at least 3 of these known column tokens.
        var known = new[]
        {
        "التاريخ", "Date",
        "معرّف السائق", "معرف السائق", "driverId", "Driver ID",
        "WorkingId", "Working ID", "معرف العمل",
        "المشرف", "Supervisor",
        "ملخص الاتصال", "Shift Summary",
        "هل أنت في الوردية", "IsInShift", "In Shift",
        "وقت اتصال", "Connection Time",
        "المهام", "Tasks Delivered", "Tasks"
    };

        for (int i = 1; i <= Math.Min(10, worksheet.RowsUsed().Count()); i++)
        {
            var row = worksheet.Row(i);
            var cells = row.CellsUsed()
                .Select(c => c.IsMerged()
                    ? c.MergedRange().FirstCell().GetString().Trim()
                    : c.GetString().Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            int hits = cells.Count(cv =>
                known.Any(k =>
                    cv.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                    cv.Replace(" ", "").Equals(k.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    cv.Contains(k, StringComparison.OrdinalIgnoreCase)));

            if (hits >= 3) return row;
        }

        // Last-resort: row with the most non-empty cells in the first 5 rows.
        return worksheet.RowsUsed()
            .Take(5)
            .OrderByDescending(r => r.CellsUsed().Count())
            .FirstOrDefault();
    }

    // ── Column mapping ────────────────────────────────────────────────────────

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

    private KeetaShiftColumnMapping BuildKeetaShiftColumnMapping(IXLRow headerRow)
    {
        var m = new KeetaShiftColumnMapping();
        var cells = headerRow.CellsUsed().ToList();

        m.DateCol = FindColumn(cells,
            "التاريخ", "Date", "تاريخ", "report_date");

        // The driver-id column may be the platform ID OR an explicit WorkingId column.
        m.DriverIdCol = FindColumn(cells,
            "معرّد السائق", "معرّف السائق", "معرف السائق",
            "driverId", "Driver ID", "DriverId",
            "WorkingId", "Working ID", "معرف العمل", "رقم العمل",
            "driver_id", "working_id");

        // A dedicated WorkingId column (optional – overrides DriverId for FK matching).
        m.WorkingIdCol = FindColumn(cells,
            "WorkingId", "Working ID", "معرف العمل", "رقم العمل");

        m.SupervisorCol = FindColumn(cells,
            "المشرف", "Supervisor", "supervisor");

        // The full pipe-separated shift summary.
        m.ShiftSummaryCol = FindColumn(cells,
            "ملخص الاتصال", "Shift Summary", "Shift Period",
            "فترة الوردية_ملخص الاتصال", "shift_summary",
            "شرح الوردية", "تفاصيل الوردية");

        m.IsInShiftCol = FindColumn(cells,
            "هل أنت في الوردية", "هل أنت في الوردية؟",
            "IsInShift", "Is In Shift", "In Shift", "Active",
            "فترة الوردية_هل أنت في الوردية؟", "in_shift");

        m.ConnectionTimeCol = FindColumn(cells,
            "وقت اتصال السائقين", "وقت الاتصال",
            "Connection Time", "ConnectionTime",
            "فترة الوردية_وقت اتصال السائقين عبر تطبيق السائق",
            "connection_time");

        m.TasksDeliveredCol = FindColumn(cells,
            "المهام التي تم تسليمها", "المهام", "Tasks Delivered", "Tasks",
            "أحجام المهام_المهام التي تم تسليمها",
            "tasks_delivered", "Deliveries");

        var missing = new List<string>();
        if (m.DateCol == 0) missing.Add("التاريخ / Date");
        if (m.DriverIdCol == 0) missing.Add("معرّف السائق / WorkingId / Driver ID");

        m.IsValid = !missing.Any();
        m.ErrorMessage = missing.Any()
            ? $"Required columns not found: {string.Join(", ", missing)}"
            : null;

        return m;
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

    // ── Row parser ────────────────────────────────────────────────────────────

    private KeetaShiftRowData ParseKeetaShiftRow(
        IXLRow row,
        KeetaShiftColumnMapping map,
        int rowNumber)
    {
        var d = new KeetaShiftRowData { RowNumber = rowNumber };

        try
        {
            // Date (YYYYMMDD or any standard format)
            var dateStr = GetCellValue(row, map.DateCol);
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                d.IsValid = false; d.ErrorMessage = "Date cell is empty"; return d;
            }
            if (!TryParseKeetaDate(dateStr.Trim(), out DateOnly reportDate))
            {
                d.IsValid = false; d.ErrorMessage = $"Unrecognised date: '{dateStr}'"; return d;
            }
            d.ReportDate = reportDate;

            // Platform Driver ID (always required)
            d.PlatformDriverId = GetCellValue(row, map.DriverIdCol)?.Trim();
            if (string.IsNullOrWhiteSpace(d.PlatformDriverId))
            {
                d.IsValid = false; d.ErrorMessage = "Driver ID / Working ID cell is empty"; return d;
            }

            // Optional explicit WorkingId column (higher priority for FK matching)
            if (map.WorkingIdCol > 0)
            {
                var wid = GetCellValue(row, map.WorkingIdCol)?.Trim();
                if (!string.IsNullOrWhiteSpace(wid)) d.WorkingId = wid;
            }

            // Supervisor
            d.Supervisor = map.SupervisorCol > 0
                ? GetCellValue(row, map.SupervisorCol)?.Trim()
                : null;
            if (d.Supervisor is "No Supervisor" or "-") d.Supervisor = null;

            // Shift summary (pipe-separated slot string)
            d.RawShiftSummary = map.ShiftSummaryCol > 0
                ? GetCellValue(row, map.ShiftSummaryCol)?.Trim()
                : null;

            // Is in shift (Yes / No)
            var inShiftStr = (map.IsInShiftCol > 0
                ? GetCellValue(row, map.IsInShiftCol)
                : null)?.Trim() ?? string.Empty;

            d.IsInShift =
                inShiftStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                inShiftStr.Equals("نعم", StringComparison.OrdinalIgnoreCase) ||
                inShiftStr == "1" ||
                inShiftStr.Equals("True", StringComparison.OrdinalIgnoreCase);

            // Connection time
            d.ConnectionTimeRaw = map.ConnectionTimeCol > 0
                ? GetCellValue(row, map.ConnectionTimeCol)?.Trim()
                : null;
            d.ConnectionMinutes = ParseArabicDurationToMinutes(d.ConnectionTimeRaw);

            // Tasks delivered
            var tasksStr = map.TasksDeliveredCol > 0
                ? GetCellValue(row, map.TasksDeliveredCol)
                : "0";
            int.TryParse(tasksStr, out int tasks);
            d.TasksDelivered = tasks;

            d.IsValid = true;
        }
        catch (Exception ex)
        {
            d.IsValid = false;
            d.ErrorMessage = $"Row parsing error: {ex.Message}";
        }

        return d;
    }

    // ── Keeta date parser ─────────────────────────────────────────────────────

    /// <summary>
    /// Handles the Keeta platform's YYYYMMDD integer format as well as
    /// all standard date string formats.
    /// </summary>
    private static bool TryParseKeetaDate(string raw, out DateOnly result)
    {
        result = DateOnly.MinValue;

        // YYYYMMDD (e.g. "20260531" — the format in the sample data)
        if (raw.Length == 8 && raw.All(char.IsDigit))
        {
            if (int.TryParse(raw[..4], out int y) &&
                int.TryParse(raw[4..6], out int mo) &&
                int.TryParse(raw[6..8], out int dy))
            {
                try { result = new DateOnly(y, mo, dy); return true; }
                catch { /* fall through */ }
            }
        }

        // Standard DateOnly.TryParse covers ISO8601, common locale formats, etc.
        if (DateOnly.TryParse(raw, out result)) return true;

        // Additional explicit formats for resilience.
        foreach (var fmt in new[]
            { "dd/MM/yyyy", "dd-MM-yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" })
        {
            if (DateTime.TryParseExact(raw, fmt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                result = DateOnly.FromDateTime(dt);
                return true;
            }
        }

        return false;
    }


    // ── Add this method to the MonthlyValidityService class ──
    // ── Also add to IMonthlyValidityService interface ──

    public async Task<Result<AllKeetaShiftsResponse>> GetAllKeetaDriverShiftsAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        string? platformDriverId = null)
    {
        try
        {
            var query = _db.KeetaDriverShifts
                .Include(k => k.ShiftSlots)
                .Include(k => k.Rider)
                    .ThenInclude(r => r!.Employee)
                .Include(k => k.Rider)
                    .ThenInclude(r => r!.Company)
                .AsNoTracking();

            if (from.HasValue)
                query = query.Where(k => k.ReportDate >= from.Value);
            if (to.HasValue)
                query = query.Where(k => k.ReportDate <= to.Value);
            if (!string.IsNullOrWhiteSpace(platformDriverId))
                query = query.Where(k =>
                    k.PlatformDriverId == platformDriverId ||
                    k.WorkingId == platformDriverId);

            var allShifts = await query
                .OrderBy(k => k.PlatformDriverId)
                .ThenBy(k => k.ReportDate)
                .ToListAsync();

            if (allShifts.Count == 0)
                return Result.Success(new AllKeetaShiftsResponse(
                    TotalRiders: 0,
                    TotalShiftRecords: 0,
                    EarliestDate: null,
                    LatestDate: null,
                    Riders: [],
                    RetrievedAt: DateTime.UtcNow.AddHours(3)
                ));

            // Group by PlatformDriverId — one group = one rider's full timeline
            var grouped = allShifts
                .GroupBy(k => k.PlatformDriverId)
                .Select(g =>
                {
                    var first = g.First();
                    var rider = first.Rider;

                    var days = g
                        .OrderBy(k => k.ReportDate)
                        .Select(k => new KeetaRiderDayDetail(
                            ReportDate: k.ReportDate,
                            IsInShift: k.IsInShift,
                            TasksDelivered: k.TasksDelivered,
                            ConnectionMinutes: k.TotalConnectionMinutes,
                            ConnectionTimeRaw: k.TotalConnectionTimeRaw,
                            QualifiedSlotsCount: k.QualifiedSlotsCount,
                            Slots: k.ShiftSlots
                                .OrderBy(s => s.StartTime)
                                .Select(s => new KeetaSlotDetail(
                                    s.SlotKey,
                                    s.DurationRaw,
                                    s.DurationMinutes,
                                    s.SlotOrder))
                                .ToList(),
                            CreatedAt: k.CreatedAt,
                            UpdatedAt: k.UpdatedAt
                        ))
                        .ToList();

                    return new KeetaRiderShiftSummary(
                        RiderId: first.RiderId,
                        PlatformDriverId: first.PlatformDriverId,
                        WorkingId: first.WorkingId,
                        RiderNameAR: rider?.Employee?.NameAR,
                        RiderNameEN: rider?.Employee?.NameEN,
                        CompanyName: rider?.Company?.Name,
                        Supervisor: first.Supervisor,
                        TotalDays: days.Count,
                        TotalInShiftDays: days.Count(d => d.IsInShift),
                        TotalTasksDelivered: days.Sum(d => d.TasksDelivered),
                        TotalConnectionMinutes: days.Sum(d => d.ConnectionMinutes),
                        Days: days
                    );
                })
                .ToList();

            return Result.Success(new AllKeetaShiftsResponse(
                TotalRiders: grouped.Count,
                TotalShiftRecords: allShifts.Count,
                EarliestDate: allShifts.Min(k => k.ReportDate),
                LatestDate: allShifts.Max(k => k.ReportDate),
                Riders: grouped,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<AllKeetaShiftsResponse>(
                new Error("RetrievalError",
                    $"Failed to retrieve Keeta shift data: {ex.Message}", 500));
        }
    }
    // ── Arabic duration → minutes ─────────────────────────────────────────────

    /// <summary>
    /// Converts Arabic time-unit strings to total minutes (seconds are dropped).
    /// 
    /// Examples
    ///   "3 س 52 د"    →  3×60 + 52  = 232
    ///   "4 س"         →  4×60       = 240
    ///   "8 د 24 ث"    →  8          (seconds ignored)
    ///   "1 س 3 د 5 ث" →  63
    ///   "0 ث"         →  0
    /// </summary>
    private static int ParseArabicDurationToMinutes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        int minutes = 0;
        var tokens = raw.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (!int.TryParse(tokens[i], out int value)) continue;

            switch (tokens[i + 1])
            {
                case "س": minutes += value * 60; break; // ساعة = hour
                case "د": minutes += value; break; // دقيقة = minute
                                                   // "ث" (ثانية = second) intentionally skipped
            }
            i++; // consume the unit token
        }

        return minutes;
    }

    // ── Qualified slot parser ─────────────────────────────────────────────────

    /// <summary>
    /// Parses the raw pipe-separated shift summary, extracts every slot that is
    /// both "On-Shift" and "qualified", then returns the top-3 by duration
    /// (most minutes worked). Ties are broken by earlier start time.
    /// The returned list is re-ordered chronologically.
    ///
    /// Input example:
    ///   "00:00-03:00,Off-Shift,3 س|03:00-08:00,Off-Shift,3 س|
    ///    08:00-12:00,Off-Shift,3 د|12:00-16:00,On-Shift,4 س,qualified|
    ///    16:00-20:00,On-Shift,4 س,qualified|20:00-24:00,On-Shift,4 س,qualified"
    /// </summary>
    private static List<KeetaSlotData> ParseQualifiedKeetaSlots(string? rawSummary)
    {
        if (string.IsNullOrWhiteSpace(rawSummary)) return [];

        var qualified = new List<KeetaSlotData>();
        var segments = rawSummary.Split('|', StringSplitOptions.RemoveEmptyEntries);
        int slotOrder = 0;

        foreach (var seg in segments)
        {
            slotOrder++;
            var parts = seg.Trim().Split(',');

            // Minimum: timeRange, shiftType, duration
            if (parts.Length < 3) continue;

            string timeRange = parts[0].Trim();         // "08:00-12:00"
            string shiftType = parts[1].Trim();          // "On-Shift"
            string durationRaw = parts[2].Trim();        // "3 س 52 د"

            bool isOnShift = shiftType.Equals("On-Shift", StringComparison.OrdinalIgnoreCase);
            bool isQualified = parts.Length >= 4 &&
                parts[3].Trim().Equals("qualified", StringComparison.OrdinalIgnoreCase);

            // We only store On-Shift + qualified slots
            if (!isOnShift || !isQualified) continue;

            // Parse time range "08:00-12:00"
            var dash = timeRange.IndexOf('-');
            if (dash < 0) continue;

            var startStr = timeRange[..dash].Trim();
            var endStr = timeRange[(dash + 1)..].Trim();

            if (!TimeOnly.TryParse(startStr, out TimeOnly startTime) ||
                !TimeOnly.TryParse(endStr, out TimeOnly endTime))
                continue;

            int durationMinutes = ParseArabicDurationToMinutes(durationRaw);

            qualified.Add(new KeetaSlotData
            {
                SlotKey = timeRange,
                StartTime = startTime,
                EndTime = endTime,
                IsOnShift = true,
                IsQualified = true,
                DurationRaw = durationRaw,
                DurationMinutes = durationMinutes,
                SlotOrder = slotOrder
            });
        }

        // ── Selection: top-3 by duration (tie-break: earlier slot wins) ────────
        return qualified
            .OrderByDescending(s => s.DurationMinutes)
            .ThenBy(s => s.StartTime)
            .Take(3)
            .OrderBy(s => s.StartTime)   // re-sort chronologically for storage
            .ToList();
    }

    // ── Entity mapper ─────────────────────────────────────────────────────────

    private static KeetaShiftSlot MapSlot(KeetaSlotData s) => new()
    {
        SlotKey = s.SlotKey,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        IsOnShift = s.IsOnShift,
        IsQualified = s.IsQualified,
        DurationRaw = s.DurationRaw,
        DurationMinutes = s.DurationMinutes,
        SlotOrder = s.SlotOrder
    };

    // ════════════════════════════════════════════════════════════════════════
    //  INTERNAL CLASSES
    // ════════════════════════════════════════════════════════════════════════

    internal class KeetaShiftColumnMapping
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public int DateCol { get; set; }
        public int DriverIdCol { get; set; }        // معرّف السائق (platform or working ID)
        public int WorkingIdCol { get; set; }       // explicit WorkingId column (optional)
        public int SupervisorCol { get; set; }
        public int ShiftSummaryCol { get; set; }    // pipe-separated slot string
        public int IsInShiftCol { get; set; }       // "Yes" / "No"
        public int ConnectionTimeCol { get; set; }  // "18 س 3 د"
        public int TasksDeliveredCol { get; set; }
    }

    internal class KeetaShiftRowData
    {
        public int RowNumber { get; set; }
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public DateOnly? ReportDate { get; set; }
        public string? PlatformDriverId { get; set; }
        public string? WorkingId { get; set; }      // from explicit WorkingId column if present
        public string? Supervisor { get; set; }
        public bool IsInShift { get; set; }
        public string? ConnectionTimeRaw { get; set; }
        public int ConnectionMinutes { get; set; }
        public int TasksDelivered { get; set; }
        public string? RawShiftSummary { get; set; }
    }

    internal class KeetaSlotData
    {
        public string SlotKey { get; set; } = string.Empty;
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool IsOnShift { get; set; }
        public bool IsQualified { get; set; }
        public string DurationRaw { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public int SlotOrder { get; set; }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────
    //  GET ALL
    // ─────────────────────────────────────────────────────────────

    public async Task<Result<AllRidersValidityResponse>> GetAllRidersValidityAsync(
        int? year = null)
    {
        try
        {
            // ── 1. Load validity records (all years OR filtered year) ─────
            var validityQuery = _db.RiderMonthlyValidities.AsNoTracking();

            if (year.HasValue)
                validityQuery = validityQuery.Where(v => v.Year == year.Value);

            var validityRecords = await validityQuery.ToListAsync();

            // ── 2. Determine available years and build (year, month) ranges ─
            var today = DateTime.Now;

            var availableYears = validityRecords
                .Select(v => v.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            // For each year: start = earliest month in DB, end = today's month if current year else 12
            var yearRanges = availableYears.ToDictionary(
                y => y,
                y =>
                {
                    int start = validityRecords.Where(v => v.Year == y).Min(v => v.Month);
                    int end = y == today.Year ? today.Month : 12;
                    return (Start: start, End: end);
                });

            // ── 3. Load only riders who have validity records ─────────────
            var iqamasWithRecords = validityRecords
                .Select(v => v.EmployeeIqamaNo)
                .Distinct()
                .ToHashSet();

            var riders = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .Where(r => iqamasWithRecords.Contains(r.EmployeeIqamaNo))
                .AsNoTracking()
                .ToListAsync();

            // Build validity lookup: (iqamaNo, year, month) → record
            var validityMap = validityRecords
                .ToDictionary(v => (v.EmployeeIqamaNo, v.Year, v.Month), v => v);

            // ── 4. Build month details for every rider ────────────────────
            var riderSummaries = riders.Select(rider =>
            {
                var monthDetails = new List<MonthValidityDetail>();

                foreach (var y in availableYears)
                {
                    var (start, end) = yearRanges[y];

                    for (int m = start; m <= end; m++)
                    {
                        validityMap.TryGetValue((rider.EmployeeIqamaNo, y, m), out var validity);
                        monthDetails.Add(BuildMonthDetail(y, m, validity));
                    }
                }

                return new RiderValiditySummary(
                    IqamaNo: rider.EmployeeIqamaNo,
                    NameAR: rider.Employee.NameAR,
                    NameEN: rider.Employee.NameEN,
                    WorkingId: rider.WorkingId,
                    CompanyName: rider.Company?.Name,
                    Months: monthDetails
                );
            }).ToList();

            // ── 5. Aggregate counters ─────────────────────────────────────
            int totalValid = validityRecords.Count(v => v.Status == ValidityStatus.Valid);
            int totalInvalid = validityRecords.Count(v => v.Status == ValidityStatus.Invalid);
            int totalFreelancer = validityRecords.Count(v => v.Status == ValidityStatus.Freelancer);
            int unclassified = riders.Count(r =>
                !validityRecords.Any(v => v.EmployeeIqamaNo == r.EmployeeIqamaNo));

            return Result.Success(new AllRidersValidityResponse(
                TotalRiders: riders.Count,
                TotalValidRecords: totalValid,
                TotalInvalidRecords: totalInvalid,
                TotalFreelancerRecords: totalFreelancer,
                TotalUnclassifiedRiders: unclassified,
                AvailableYears: availableYears,
                Riders: riderSummaries,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<AllRidersValidityResponse>(
                new Error("RetrievalError",
                    $"Failed to retrieve validity data: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  GET BY IQAMA
    // ─────────────────────────────────────────────────────────────

    public async Task<Result<RiderValidityResponse>> GetRiderValidityByIqamaAsync(
        long iqamaNo,
        int? year = null)
    {
        try
        {
            // ── 1. Find rider ─────────────────────────────────────────────
            var rider = await _db.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
            {
                var empExists = await _db.Employees.AnyAsync(e => e.IqamaNo == iqamaNo);
                return Result.Failure<RiderValidityResponse>(
                    new Error(
                        empExists ? "NoRiderDetails" : "NotFound",
                        empExists
                            ? $"Employee {iqamaNo} found but has no RiderDetails record"
                            : $"No employee found with IqamaNo {iqamaNo}",
                        404));
            }

            // ── 2. Load validity records (all years OR filtered year) ─────
            var validityQuery = _db.RiderMonthlyValidities
                .Where(v => v.EmployeeIqamaNo == iqamaNo)
                .AsNoTracking();

            if (year.HasValue)
                validityQuery = validityQuery.Where(v => v.Year == year.Value);

            var validityRecords = await validityQuery.ToListAsync();

            // ── 3. Determine available years and month ranges ─────────────
            var today = DateTime.Now;

            var availableYears = validityRecords
                .Select(v => v.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            var yearRanges = availableYears.ToDictionary(
                y => y,
                y =>
                {
                    int start = validityRecords.Where(v => v.Year == y).Min(v => v.Month);
                    int end = y == today.Year ? today.Month : 12;
                    return (Start: start, End: end);
                });

            var validityMap = validityRecords
                .ToDictionary(v => (v.Year, v.Month), v => v);

            // ── 4. Build month details ────────────────────────────────────
            var monthDetails = new List<MonthValidityDetail>();

            foreach (var y in availableYears)
            {
                var (start, end) = yearRanges[y];
                for (int m = start; m <= end; m++)
                {
                    validityMap.TryGetValue((y, m), out var validity);
                    monthDetails.Add(BuildMonthDetail(y, m, validity));
                }
            }

            return Result.Success(new RiderValidityResponse(
                IqamaNo: iqamaNo,
                NameAR: rider.Employee.NameAR,
                NameEN: rider.Employee.NameEN,
                WorkingId: rider.WorkingId,
                CompanyName: rider.Company?.Name,
                AvailableYears: availableYears,
                Months: monthDetails,
                RetrievedAt: DateTime.UtcNow.AddHours(3)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderValidityResponse>(
                new Error("RetrievalError",
                    $"Failed to retrieve validity data: {ex.Message}", 500));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private static MonthValidityDetail BuildMonthDetail(
        int year, int month, RiderMonthlyValidity? validity)
    {
        string statusLabel = validity?.Status switch
        {
            ValidityStatus.Valid => "صالح",
            ValidityStatus.Invalid => "غير صالح",
            ValidityStatus.Freelancer => "فري لانسر",
            _ => "غير مصنف"
        };

        return new MonthValidityDetail(
            Year: year,
            Month: month,
            MonthName: MonthNames.GetValueOrDefault(month, month.ToString()),
            Status: validity?.Status,
            StatusLabel: statusLabel,
            RecordedOrders: validity?.TotalOrders ?? 0
        );
    }
}